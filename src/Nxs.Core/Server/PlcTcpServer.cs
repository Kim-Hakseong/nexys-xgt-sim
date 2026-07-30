using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Nxs.Core.Diagnostics;
using Nxs.Core.Protocol;
using Nxs.Core.Time;

namespace Nxs.Core.Server;

/// <summary>
/// 멀티클라이언트 TCP 서버 (PRD X-03 전송 절반).
/// </summary>
/// <remarks>
/// <para>
/// 프로토콜 지식은 <see cref="IFrameCodec"/>에만 있다 — 이 클래스는 바이트를 프레임으로 모아
/// 코덱에 넘기고 응답 바이트를 돌려줄 뿐이다. 따라서 XGT 프레임 근거가 도착해도(⛔ M2)
/// 이 파일은 수정 대상이 아니다.
/// </para>
/// <para>
/// 연결마다 자기 <see cref="StreamFrameAssembler"/>를 가지므로 부분 수신은 연결별로 독립이다.
/// 프레이밍 위반은 해당 연결만 닫는다(수신 상태를 재동기화할 수 없기 때문). 요청 거절은
/// 연결을 유지한 채 에러 응답으로 답한다 — 실장비와 같은 태도.
/// </para>
/// <para>모든 I/O는 비동기이며 UI 스레드를 블로킹하지 않는다 (CLAUDE.md §3).</para>
/// </remarks>
public sealed class PlcTcpServer : IAsyncDisposable
{
    private readonly IFrameCodec _codec;
    private readonly PlcTcpServerOptions _options;
    private readonly ITimeSource _time;
    private readonly ITrafficSink? _traffic;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _clientSequence;
    private bool _disposed;

    /// <summary>서버를 만든다.</summary>
    /// <param name="codec">프레임 코덱. 프로토콜 세부를 전담한다.</param>
    /// <param name="options">바인딩·한계 설정.</param>
    /// <param name="timeSource">시간 원천. 기본은 시스템 시계.</param>
    /// <param name="trafficSink">트래픽 기록처. null이면 기록하지 않는다.</param>
    public PlcTcpServer(
        IFrameCodec codec,
        PlcTcpServerOptions options,
        ITimeSource? timeSource = null,
        ITrafficSink? trafficSink = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _codec = codec;
        _options = options;
        _time = timeSource ?? new SystemTimeSource();
        _traffic = trafficSink;
    }

    /// <summary>실제로 바인딩된 종점. 미시작이면 null. 포트 0으로 시작하면 OS 배정 포트가 보인다.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    /// <summary>수신 대기 중인지.</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>현재 접속 수.</summary>
    public int ConnectedClientCount => _clients.Count;

    /// <summary>수신을 시작한다.</summary>
    /// <exception cref="InvalidOperationException">이미 시작되었을 때.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_listener is not null)
            {
                throw new InvalidOperationException("서버가 이미 시작되었습니다");
            }

            var listener = new TcpListener(_options.BindAddress, _options.Port);
            listener.Start(_options.Backlog);

            _listener = listener;
            LocalEndPoint = (IPEndPoint)listener.LocalEndpoint;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token), CancellationToken.None);

            Note($"수신 시작 {LocalEndPoint}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>수신을 멈추고 모든 연결을 닫는다. 멈춘 뒤 다시 시작할 수 있다.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        Task? acceptLoop;
        try
        {
            if (_listener is null)
            {
                return;
            }

            var endpoint = LocalEndPoint;
            _listener.Stop();
            _listener = null;
            LocalEndPoint = null;

            if (_cts is not null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            foreach (var (id, client) in _clients.ToArray())
            {
                CloseClient(id, client);
            }

            acceptLoop = _acceptLoop;
            _acceptLoop = null;
            Note($"수신 정지 {endpoint}");
        }
        finally
        {
            _lifecycle.Release();
        }

        if (acceptLoop is not null)
        {
            await acceptLoop.ConfigureAwait(false);
        }

        _cts?.Dispose();
        _cts = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycle.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            if (_options.MaxClients is { } max && _clients.Count >= max)
            {
                Note($"접속 거부 (동시 접속 한계 {max})");
                client.Close();
                continue;
            }

            var id = NextClientId(client);
            _clients[id] = client;
            client.NoDelay = true;

            _ = Task.Run(() => ServeClientAsync(id, client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ServeClientAsync(string id, TcpClient client, CancellationToken cancellationToken)
    {
        Note("접속", id);
        var assembler = new StreamFrameAssembler(_codec.LengthRule, _codec.MaxFrameLength);
        var buffer = new byte[_options.ReceiveBufferSize];

        try
        {
            var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                IReadOnlyList<byte[]> frames;
                try
                {
                    frames = assembler.Push(buffer.AsSpan(0, read));
                }
                catch (FramingException ex)
                {
                    // 수신 상태를 신뢰할 수 없으므로 이 연결만 닫는다.
                    Note($"프레이밍 위반 — 연결 종료: {ex.Message}", id, PlcErrorReason.InvalidAddress);
                    break;
                }

                foreach (var frame in frames)
                {
                    var exchange = _codec.Handle(frame);

                    Record(TrafficDirection.Rx, id, frame, exchange.RequestSummary, exchange.Reason);

                    if (exchange.IsSilent)
                    {
                        continue;
                    }

                    await stream.WriteAsync(exchange.ResponseFrame, cancellationToken).ConfigureAwait(false);
                    Record(TrafficDirection.Tx, id, exchange.ResponseFrame, exchange.ResponseSummary, exchange.Reason);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정지 요청 — 정상 경로.
        }
        catch (IOException)
        {
            // 상대가 끊음 — 정상 경로.
        }
        catch (ObjectDisposedException)
        {
            // 정지 중 스트림이 닫힘 — 정상 경로.
        }
        catch (SocketException)
        {
            // 전송 계층 오류 — 이 연결만 종료.
        }
        finally
        {
            if (_clients.TryRemove(id, out _))
            {
                Note("접속 해제", id);
            }

            client.Dispose();
        }
    }

    private void CloseClient(string id, TcpClient client)
    {
        if (_clients.TryRemove(id, out _))
        {
            try
            {
                client.Close();
            }
            catch (SocketException)
            {
                // 이미 닫힌 소켓 — 무시.
            }
        }
    }

    private string NextClientId(TcpClient client)
    {
        var n = Interlocked.Increment(ref _clientSequence);
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        return $"#{n} {remote}";
    }

    private void Record(
        TrafficDirection direction, string clientId, byte[] raw, string summary, PlcErrorReason reason)
        => _traffic?.Record(new TrafficEvent
        {
            Timestamp = _time.UtcNow,
            Direction = direction,
            ClientId = clientId,
            Raw = raw,
            Summary = summary,
            Reason = reason,
        });

    private void Note(string summary, string clientId = "-", PlcErrorReason reason = PlcErrorReason.None)
        => _traffic?.Record(new TrafficEvent
        {
            Timestamp = _time.UtcNow,
            Direction = TrafficDirection.Note,
            ClientId = clientId,
            Summary = summary,
            Reason = reason,
        });
}
