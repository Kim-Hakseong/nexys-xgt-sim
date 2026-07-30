using System.Net;
using Nxs.Core.Diagnostics;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Server;
using Nxs.TestKit;

namespace Nxs.Integration.Tests;

/// <summary>
/// PRD M3 DoD — 읽기/쓰기/연속/오류 전 케이스 + 멀티클라이언트 왕복.
/// 코덱 자리는 합성 코덱(TestOnlyFrameCodec)이 채운다: XGT 프레임 근거가 없으므로(⛔ M2)
/// 여기서 검증하는 것은 **서버 파이프라인**이다 — 전송·프레이밍·디스패치·응답·멀티클라이언트.
/// </summary>
public class PlcTcpServerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private PlcMemory _memory = null!;
    private PlcTcpServer _server = null!;
    private RecordingTrafficSink _sink = null!;
    private FakeTimeSource _time = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 4096 });
        _sink = new RecordingTrafficSink();
        _time = new FakeTimeSource();
        _server = new PlcTcpServer(
            new TestOnlyFrameCodec(new PlcRequestExecutor(_memory)),
            new PlcTcpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 },
            _time,
            _sink);

        await _server.StartAsync(CancellationToken.None);
        _port = _server.LocalEndPoint!.Port;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private Task<PlcTestClient> ConnectAsync() => PlcTestClient.ConnectAsync("127.0.0.1", _port);

    [Fact]
    public void ServerReportsBoundEndpointWithOsAssignedPort()
    {
        Assert.NotNull(_server.LocalEndPoint);
        Assert.True(_server.LocalEndPoint!.Port > 0);
        Assert.Equal(IPAddress.Loopback, _server.LocalEndPoint.Address);
        Assert.True(_server.IsRunning);
    }

    [Fact]
    public async Task IndividualReadReturnsMemoryContent()
    {
        _memory.WriteScalar(IecAddress.Parse("%MW10"), 0xBEEF);
        await using var client = await ConnectAsync();

        var res = await client.ReadIndividualAsync("%MW10");

        Assert.True(res.IsSuccess);
        Assert.Equal(0xBEEF, res.FirstWord);
    }

    [Fact]
    public async Task IndividualWriteMutatesMemory()
    {
        await using var client = await ConnectAsync();

        var res = await client.WriteIndividualAsync(("%MW20", new byte[] { 0x34, 0x12 }));

        Assert.True(res.IsSuccess);
        Assert.Equal(0x1234u, _memory.ReadScalar(IecAddress.Parse("%MW20")));
    }

    [Fact]
    public async Task ContinuousReadReturnsRequestedBytes()
    {
        _memory.WriteWords(MemoryArea.M, 0, new ushort[] { 0x1122, 0x3344, 0x5566 });
        await using var client = await ConnectAsync();

        var res = await client.ReadContinuousAsync("%MW0", 6);

        Assert.True(res.IsSuccess);
        Assert.Equal("22 11 44 33 66 55", res.FirstBlockHex);
    }

    [Fact]
    public async Task ContinuousWriteMutatesMemory()
    {
        await using var client = await ConnectAsync();

        var res = await client.WriteContinuousAsync("%MW100", new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

        Assert.True(res.IsSuccess);
        Assert.Equal(0xBBAAu, _memory.ReadScalar(IecAddress.Parse("%MW100")));
        Assert.Equal(0xDDCCu, _memory.ReadScalar(IecAddress.Parse("%MW101")));
    }

    [Fact]
    public async Task OutOfRangeRequestGetsErrorResponseNotDisconnect()
    {
        await using var client = await ConnectAsync();

        var res = await client.ReadContinuousAsync("%MB4090", 16);

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.RangeExceeded, res.Reason);

        // 오류 후에도 같은 연결이 계속 동작해야 한다 (실장비는 거절하고 연결을 유지한다).
        var ok = await client.ReadIndividualAsync("%MW0");
        Assert.True(ok.IsSuccess);
    }

    [Fact]
    public async Task UnparsableAddressGetsErrorResponse()
    {
        await using var client = await ConnectAsync();

        var res = await client.RequestAsync(TestOnlyFrameCodec.BuildReadIndividual("%ZW10"));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.InvalidAddress, res.Reason);
    }

    [Fact]
    public async Task RequestSentOneByteAtATimeStillGetsResponse()
    {
        _memory.WriteScalar(IecAddress.Parse("%MW7"), 0x0A0B);
        await using var client = await ConnectAsync();

        // 서버 측 부분 수신 불변 — 청크 경계와 무관하게 동일 결과.
        await client.SendInChunksAsync(TestOnlyFrameCodec.BuildReadIndividual("%MW7"), chunkSize: 1);
        var res = TestOnlyFrameCodec.DecodeResponse(await client.ReceiveFrameAsync());

        Assert.True(res.IsSuccess);
        Assert.Equal(0x0A0B, res.FirstWord);
    }

    [Fact]
    public async Task TwoRequestsPipelinedInOneWriteGetTwoResponsesInOrder()
    {
        _memory.WriteScalar(IecAddress.Parse("%MW1"), 0x1111);
        _memory.WriteScalar(IecAddress.Parse("%MW2"), 0x2222);
        await using var client = await ConnectAsync();

        var both = TestOnlyFrameCodec.BuildReadIndividual("%MW1")
            .Concat(TestOnlyFrameCodec.BuildReadIndividual("%MW2")).ToArray();
        await client.SendRawAsync(both);

        var first = TestOnlyFrameCodec.DecodeResponse(await client.ReceiveFrameAsync());
        var second = TestOnlyFrameCodec.DecodeResponse(await client.ReceiveFrameAsync());

        Assert.Equal(0x1111, first.FirstWord);
        Assert.Equal(0x2222, second.FirstWord);
    }

    [Fact]
    public async Task ThreeConcurrentClientsEachGetTheirOwnCorrectResponses()
    {
        for (var i = 0; i < 3; i++)
        {
            _memory.WriteScalar(IecAddress.Parse($"%MW{200 + i}"), (uint)(0x0100 * (i + 1)));
        }

        var clients = new List<PlcTestClient>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                clients.Add(await ConnectAsync());
            }

            await WaitForClientCountAsync(3);

            // 각 클라이언트가 자기 주소를 30회 왕복 — 응답 혼선이 있으면 값이 어긋난다.
            var work = clients.Select((c, i) => Task.Run(async () =>
            {
                for (var round = 0; round < 30; round++)
                {
                    var res = await c.ReadIndividualAsync($"%MW{200 + i}");
                    Assert.True(res.IsSuccess);
                    Assert.Equal((ushort)(0x0100 * (i + 1)), res.FirstWord);
                }
            })).ToArray();

            await Task.WhenAll(work);
        }
        finally
        {
            foreach (var c in clients)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ConcurrentClientsWritingDifferentAddressesAllLand()
    {
        var clients = new List<PlcTestClient>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                clients.Add(await ConnectAsync());
            }

            var work = clients.Select((c, i) => Task.Run(async () =>
            {
                for (var w = 0; w < 20; w++)
                {
                    var address = $"%MW{300 + (i * 20) + w}";
                    var value = (ushort)(i * 1000 + w);
                    var res = await c.WriteIndividualAsync(
                        (address, new[] { (byte)(value & 0xFF), (byte)(value >> 8) }));
                    Assert.True(res.IsSuccess);
                }
            })).ToArray();

            await Task.WhenAll(work);

            for (var i = 0; i < 4; i++)
            {
                for (var w = 0; w < 20; w++)
                {
                    Assert.Equal(
                        (uint)(i * 1000 + w),
                        _memory.ReadScalar(IecAddress.Parse($"%MW{300 + (i * 20) + w}")));
                }
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ClientDisconnectDoesNotAffectServerOrOtherClients()
    {
        await using var survivor = await ConnectAsync();
        var doomed = await ConnectAsync();
        await WaitForClientCountAsync(2);

        await doomed.DisposeAsync();
        await WaitForClientCountAsync(1);

        Assert.True(_server.IsRunning);
        var res = await survivor.ReadIndividualAsync("%MW0");
        Assert.True(res.IsSuccess);
    }

    [Fact]
    public async Task FramingViolationClosesOnlyTheOffendingConnection()
    {
        await using var survivor = await ConnectAsync();
        var offender = await ConnectAsync();
        await WaitForClientCountAsync(2);

        // 합성 프레이밍의 매직에 맞지 않는 바이트 → 수신 상태를 신뢰할 수 없으므로 연결을 닫는다.
        await offender.SendRawAsync(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        Assert.True(await offender.WaitForCloseAsync(Timeout), "위반 연결이 닫히지 않았습니다");
        await offender.DisposeAsync();

        Assert.True(_server.IsRunning);
        Assert.True((await survivor.ReadIndividualAsync("%MW0")).IsSuccess);
    }

    [Fact]
    public async Task TrafficSinkRecordsRxAndTxWithInjectedTimestamps()
    {
        await using var client = await ConnectAsync();

        await client.ReadIndividualAsync("%MW0");
        await WaitForAsync(() => _sink.OfDirection(TrafficDirection.Tx).Count >= 1);

        var rx = Assert.Single(_sink.OfDirection(TrafficDirection.Rx));
        var tx = Assert.Single(_sink.OfDirection(TrafficDirection.Tx));

        Assert.Equal(_time.UtcNow, rx.Timestamp);
        Assert.NotEmpty(rx.Raw);
        Assert.Contains("%MW0", rx.Summary, StringComparison.Ordinal);
        Assert.False(rx.IsError);
        Assert.NotEmpty(tx.Raw);
        Assert.Equal(rx.ClientId, tx.ClientId);
    }

    [Fact]
    public async Task TrafficSinkMarksRejectedRequestAsError()
    {
        await using var client = await ConnectAsync();

        await client.ReadContinuousAsync("%MB4090", 16);
        await WaitForAsync(() => _sink.Events.Any(e => e.IsError));

        var error = _sink.Events.First(e => e.IsError);
        Assert.Equal(PlcErrorReason.RangeExceeded, error.Reason);
    }

    [Fact]
    public async Task StopAsyncClosesListenerAndConnections()
    {
        var client = await ConnectAsync();
        await WaitForClientCountAsync(1);

        await _server.StopAsync();

        Assert.False(_server.IsRunning);
        Assert.True(await client.WaitForCloseAsync(Timeout), "정지 후 연결이 닫히지 않았습니다");
        await client.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => PlcTestClient.ConnectAsync("127.0.0.1", _port));
    }

    [Fact]
    public async Task RestartAfterStopBindsAgain()
    {
        await _server.StopAsync();
        await _server.StartAsync(CancellationToken.None);
        _port = _server.LocalEndPoint!.Port;

        await using var client = await ConnectAsync();
        Assert.True((await client.ReadIndividualAsync("%MW0")).IsSuccess);
    }

    [Fact]
    public async Task StartingTwiceThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _server.StartAsync(CancellationToken.None));
    }

    private async Task WaitForClientCountAsync(int expected)
        => await WaitForAsync(() => _server.ConnectedClientCount == expected,
            () => $"연결 수 {_server.ConnectedClientCount}, 기대 {expected}");

    private static async Task WaitForAsync(Func<bool> condition, Func<string>? detail = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"조건 대기 시간 초과. {detail?.Invoke() ?? string.Empty}");
    }
}
