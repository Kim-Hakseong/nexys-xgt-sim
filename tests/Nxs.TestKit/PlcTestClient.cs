using System.Net.Sockets;
using Nxs.Core.Protocol;

namespace Nxs.TestKit;

/// <summary>
/// 시뮬레이터에 접속하는 테스트 클라이언트 (DESIGN: tests + TestClient).
/// 합성 코덱(<see cref="TestOnlyFrameCodec"/>) 포맷으로 말한다 — LabVIEW 대역이 아니라
/// 서버 계층 e2e 검증 및 UI 스모크용이다.
/// </summary>
public sealed class PlcTestClient : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly StreamFrameAssembler _assembler;
    private readonly Queue<byte[]> _pending = new();

    private PlcTestClient(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _assembler = new StreamFrameAssembler(new TestOnlyLengthPrefixFraming(), 8192);
    }

    /// <summary>서버에 접속한다.</summary>
    public static async Task<PlcTestClient> ConnectAsync(
        string host, int port, CancellationToken cancellationToken = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        tcp.NoDelay = true;
        return new PlcTestClient(tcp);
    }

    /// <summary>raw 바이트를 그대로 보낸다.</summary>
    public Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        => _stream.WriteAsync(bytes, cancellationToken).AsTask();

    /// <summary>
    /// raw 바이트를 지정 크기 조각으로 쪼개어 보낸다. 서버 측 부분 수신 처리를 자극한다.
    /// </summary>
    public async Task SendInChunksAsync(
        byte[] bytes, int chunkSize, CancellationToken cancellationToken = default)
    {
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var take = Math.Min(chunkSize, bytes.Length - offset);
            await _stream.WriteAsync(bytes.AsMemory(offset, take), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>응답 프레임 하나를 완독한다.</summary>
    /// <exception cref="IOException">프레임을 완성하기 전에 연결이 닫혔을 때.</exception>
    public async Task<byte[]> ReceiveFrameAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.Count > 0)
        {
            return _pending.Dequeue();
        }

        var buffer = new byte[4096];
        while (true)
        {
            var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("응답 프레임을 완성하기 전에 연결이 닫혔습니다");
            }

            foreach (var frame in _assembler.Push(buffer.AsSpan(0, read)))
            {
                _pending.Enqueue(frame);
            }

            if (_pending.Count > 0)
            {
                return _pending.Dequeue();
            }
        }
    }

    /// <summary>요청 프레임을 보내고 응답 하나를 받아 해석한다.</summary>
    public async Task<TestResponse> RequestAsync(
        byte[] requestFrame, CancellationToken cancellationToken = default)
    {
        await SendRawAsync(requestFrame, cancellationToken).ConfigureAwait(false);
        return TestOnlyFrameCodec.DecodeResponse(await ReceiveFrameAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>개별 읽기.</summary>
    public Task<TestResponse> ReadIndividualAsync(params string[] addresses)
        => RequestAsync(TestOnlyFrameCodec.BuildReadIndividual(addresses));

    /// <summary>개별 쓰기.</summary>
    public Task<TestResponse> WriteIndividualAsync(params (string Address, byte[] Value)[] items)
        => RequestAsync(TestOnlyFrameCodec.BuildWriteIndividual(items));

    /// <summary>연속 읽기.</summary>
    public Task<TestResponse> ReadContinuousAsync(string address, int byteCount)
        => RequestAsync(TestOnlyFrameCodec.BuildReadContinuous(address, byteCount));

    /// <summary>연속 쓰기.</summary>
    public Task<TestResponse> WriteContinuousAsync(string address, byte[] data)
        => RequestAsync(TestOnlyFrameCodec.BuildWriteContinuous(address, data));

    /// <summary>서버가 이 연결을 닫을 때까지 기다린다.</summary>
    public async Task<bool> WaitForCloseAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[256];
        try
        {
            while (true)
            {
                var read = await _stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _tcp.Dispose();
    }
}
