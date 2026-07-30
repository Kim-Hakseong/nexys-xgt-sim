using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Nxs.Core.Server;
using Nxs.TestKit;

namespace Nxs.Integration.Tests;

/// <summary>
/// XGT FEnet e2e — 실제 TCP 소켓으로 초안 프레임을 주고받는다.
/// LabVIEW 가 하게 될 왕복을 코드로 재현한 것이다(마스터 역할을 테스트가 수행).
/// </summary>
/// <remarks>
/// ⚠️ 초안 레이아웃 기준이므로 "실장비와 동일"을 증명하지는 않는다.
/// 증명하는 것: 전송 → 프레이밍 → XGT 코덱 → 메모리 → 응답 경로가 실제 소켓에서 동작한다는 것.
/// </remarks>
public class XgtFenetEndToEndTests : IAsyncLifetime
{
    private PlcMemory _memory = null!;
    private PlcTcpServer _server = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 8192 });
        _server = new PlcTcpServer(
            new XgtFenetCodec(new PlcRequestExecutor(_memory)),
            new PlcTcpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });

        await _server.StartAsync(CancellationToken.None);
        _port = _server.LocalEndPoint!.Port;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    /// <summary>LabVIEW 자리에 서는 최소 마스터.</summary>
    private sealed class XgtMaster : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private ushort _invokeId;

        private XgtMaster(TcpClient tcp)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
        }

        public static async Task<XgtMaster> ConnectAsync(int port)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            tcp.NoDelay = true;
            return new XgtMaster(tcp);
        }

        public ushort LastInvokeId { get; private set; }

        /// <summary>헤더로 감싸지 않고 바이트를 그대로 보낸다(프레이밍 위반 시험용).</summary>
        public Task SendRawAsync(byte[] bytes) => _stream.WriteAsync(bytes).AsTask();

        /// <summary>서버가 이 연결을 닫을 때까지 기다린다.</summary>
        public async Task<bool> WaitForCloseAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            var buffer = new byte[64];
            try
            {
                while (true)
                {
                    if (await _stream.ReadAsync(buffer, cts.Token) == 0)
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

        public async Task<byte[]> ExchangeAsync(byte[] data, int chunkSize = 0)
        {
            LastInvokeId = ++_invokeId;
            var frame = BuildFrame(data, LastInvokeId);

            if (chunkSize > 0)
            {
                for (var offset = 0; offset < frame.Length; offset += chunkSize)
                {
                    var take = Math.Min(chunkSize, frame.Length - offset);
                    await _stream.WriteAsync(frame.AsMemory(offset, take));
                    await _stream.FlushAsync();
                }
            }
            else
            {
                await _stream.WriteAsync(frame);
            }

            return await ReadFrameAsync();
        }

        private async Task<byte[]> ReadFrameAsync()
        {
            var header = new byte[20];
            await ReadExactlyAsync(header);
            var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(16));
            var body = new byte[dataLength];
            await ReadExactlyAsync(body);
            return [.. header, .. body];
        }

        private async Task ReadExactlyAsync(byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await _stream.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                {
                    throw new IOException("응답 완독 전에 연결이 닫혔습니다");
                }

                read += n;
            }
        }

        private static byte[] BuildFrame(byte[] data, ushort invokeId)
        {
            var frame = new byte[20 + data.Length];
            Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
            frame[12] = 0xA0;
            frame[13] = 0x33;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14), invokeId);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
            byte sum = 0;
            for (var i = 0; i < 19; i++)
            {
                sum += frame[i];
            }

            frame[19] = sum;
            data.CopyTo(frame, 20);
            return frame;
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _tcp.Dispose();
        }
    }

    private static byte[] U16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static byte[] ReadRequest(ushort dataType, params string[] names)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0054));
        b.AddRange(U16(dataType));
        b.AddRange(U16(0));
        b.AddRange(U16((ushort)names.Length));
        foreach (var n in names)
        {
            var a = Encoding.ASCII.GetBytes(n);
            b.AddRange(U16((ushort)a.Length));
            b.AddRange(a);
        }

        return b.ToArray();
    }

    private static byte[] WriteRequest(ushort dataType, string name, byte[] value)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0058));
        b.AddRange(U16(dataType));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        var a = Encoding.ASCII.GetBytes(name);
        b.AddRange(U16((ushort)a.Length));
        b.AddRange(a);
        b.AddRange(U16((ushort)value.Length));
        b.AddRange(value);
        return b.ToArray();
    }

    private static byte[] ContinuousRead(string name, ushort byteCount)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0054));
        b.AddRange(U16(0x0014));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        var a = Encoding.ASCII.GetBytes(name);
        b.AddRange(U16((ushort)a.Length));
        b.AddRange(a);
        b.AddRange(U16(byteCount));
        return b.ToArray();
    }

    private static ushort Data(byte[] frame, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20 + offset));

    [Fact]
    public async Task MasterReadsAWordOverARealSocket()
    {
        _memory.WriteScalar(IecAddress.Parse("%MW320"), 0x1234);
        await using var master = await XgtMaster.ConnectAsync(_port);

        var response = await master.ExchangeAsync(ReadRequest(0x0002, "%MW320"));

        Assert.Equal(0x0055, Data(response, 0));
        Assert.Equal(0x0000, Data(response, 6));
        Assert.Equal(0x1234, Data(response, 12));
        Assert.Equal(master.LastInvokeId, BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(14)));
        Assert.Equal(0x11, response[13]);
    }

    [Fact]
    public async Task MasterWritesAWordAndTheSimulatorMemoryChanges()
    {
        await using var master = await XgtMaster.ConnectAsync(_port);

        var response = await master.ExchangeAsync(WriteRequest(0x0002, "%MW320", [0xCD, 0xAB]));

        Assert.Equal(0x0059, Data(response, 0));
        Assert.Equal(0x0000, Data(response, 6));
        Assert.Equal(0xABCDu, _memory.ReadScalar(IecAddress.Parse("%MW320")));
    }

    [Fact]
    public async Task MasterReadsADWordAtAUserChosenAddress()
    {
        _memory.WriteScalar(IecAddress.Parse("%MD422"), 0xDEADBEEF);
        await using var master = await XgtMaster.ConnectAsync(_port);

        var response = await master.ExchangeAsync(ReadRequest(0x0003, "%MD422"));

        Assert.Equal(4, Data(response, 10));
        Assert.Equal(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(32)));
    }

    [Fact]
    public async Task MasterReadsAContinuousBlock()
    {
        _memory.WriteWords(MemoryArea.M, 100, [0x1111, 0x2222, 0x3333, 0x4444]);
        await using var master = await XgtMaster.ConnectAsync(_port);

        var response = await master.ExchangeAsync(ContinuousRead("%MW100", 8));

        Assert.Equal(0x0000, Data(response, 6));
        Assert.Equal(8, Data(response, 10));
        Assert.Equal("11 11 22 22 33 33 44 44", Hex.Format(response.AsSpan(32, 8)));
    }

    [Fact]
    public async Task MasterTogglesABitAndReadsItBack()
    {
        await using var master = await XgtMaster.ConnectAsync(_port);

        await master.ExchangeAsync(WriteRequest(0x0000, "%MX801", [0x01]));
        var response = await master.ExchangeAsync(ReadRequest(0x0000, "%MX801"));

        Assert.Equal(1, Data(response, 10));
        Assert.Equal(0x01, response[32]);
        Assert.True(_memory.ReadBit(IecAddress.Parse("%MX801")));
    }

    [Fact]
    public async Task RequestSentOneByteAtATimeStillGetsACorrectResponse()
    {
        _memory.WriteScalar(IecAddress.Parse("%MW7"), 0x0A0B);
        await using var master = await XgtMaster.ConnectAsync(_port);

        var response = await master.ExchangeAsync(ReadRequest(0x0002, "%MW7"), chunkSize: 1);

        Assert.Equal(0x0A0B, Data(response, 12));
    }

    [Fact]
    public async Task OutOfRangeReadGetsAnErrorResponseAndTheConnectionSurvives()
    {
        await using var master = await XgtMaster.ConnectAsync(_port);

        var error = await master.ExchangeAsync(ReadRequest(0x0002, "%MW99999"));
        Assert.NotEqual(0x0000, Data(error, 6));

        // 실장비처럼 연결을 유지하고 다음 요청에 정상 응답해야 한다.
        _memory.WriteScalar(IecAddress.Parse("%MW0"), 0x0077);
        var ok = await master.ExchangeAsync(ReadRequest(0x0002, "%MW0"));
        Assert.Equal(0x0000, Data(ok, 6));
        Assert.Equal(0x0077, Data(ok, 12));
    }

    [Fact]
    public async Task PollingLoopSustainsManyExchangesOnOneConnection()
    {
        // LabVIEW 폴링을 모사 — 같은 연결로 200회 왕복하며 Invoke ID 가 매번 에코되는지 확인.
        await using var master = await XgtMaster.ConnectAsync(_port);

        for (var i = 0; i < 200; i++)
        {
            _memory.WriteScalar(IecAddress.Parse("%MW500"), (uint)(i & 0xFFFF));
            var response = await master.ExchangeAsync(ReadRequest(0x0002, "%MW500"));

            Assert.Equal((ushort)(i & 0xFFFF), Data(response, 12));
            Assert.Equal(master.LastInvokeId, BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(14)));
        }
    }

    [Fact]
    public async Task ThreeMastersPollConcurrentlyWithoutCrossTalk()
    {
        var masters = new List<XgtMaster>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                masters.Add(await XgtMaster.ConnectAsync(_port));
                _memory.WriteScalar(IecAddress.Parse($"%MW{600 + i}"), (uint)(0x1000 * (i + 1)));
            }

            await Task.WhenAll(masters.Select((m, i) => Task.Run(async () =>
            {
                for (var round = 0; round < 40; round++)
                {
                    var response = await m.ExchangeAsync(ReadRequest(0x0002, $"%MW{600 + i}"));
                    Assert.Equal((ushort)(0x1000 * (i + 1)), Data(response, 12));
                }
            })));
        }
        finally
        {
            foreach (var m in masters)
            {
                await m.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task GarbageBytesCloseOnlyTheOffendingConnection()
    {
        await using var survivor = await XgtMaster.ConnectAsync(_port);
        var offender = await XgtMaster.ConnectAsync(_port);

        // Company ID 가 아닌 쓰레기를 **헤더로 감싸지 않고** 그대로 보낸다 → 프레이밍 위반.
        await offender.SendRawAsync([
            0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8,
            0xF7, 0xF6, 0xF5, 0xF4, 0xF3, 0xF2, 0xF1, 0xF0,
            0x10, 0x00, 0x00, 0x00,
        ]);

        Assert.True(
            await offender.WaitForCloseAsync(TimeSpan.FromSeconds(10)),
            "프레이밍 위반 연결이 닫히지 않았습니다");
        await offender.DisposeAsync();

        Assert.True(_server.IsRunning);
        _memory.WriteScalar(IecAddress.Parse("%MW0"), 0x1234);
        Assert.Equal(0x1234, Data(await survivor.ExchangeAsync(ReadRequest(0x0002, "%MW0")), 12));
    }

    [Fact]
    public async Task WatchAddressWrittenByTheMasterIsVisibleThroughTheWatchEntry()
    {
        // 사용자가 지정한 워치 주소가 실제 마스터 쓰기와 같은 셀을 가리키는지.
        var watch = new WatchEntry { Address = "%MD422", Label = "적산 유량", Format = WatchFormat.Hex };
        await using var master = await XgtMaster.ConnectAsync(_port);

        await master.ExchangeAsync(WriteRequest(0x0003, "%MD422", [0xEF, 0xBE, 0xAD, 0xDE]));

        var address = watch.Resolve();
        Assert.Equal("0xDEADBEEF", WatchEntry.Render(_memory.ReadScalar(address), address.Size, watch.Format));
    }
}
