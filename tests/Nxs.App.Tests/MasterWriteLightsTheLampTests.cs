using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 마스터가 쓴 값이 **화면의 디지털 램프까지** 도달하는지 — 소켓부터 LED 까지 한 번에.
/// </summary>
/// <remarks>
/// 사용자가 보고한 증상: "트래픽 로그에는 랩뷰가 보낸 것이 찍히는데 디지털 I/O 불이 안 들어온다."
/// 코덱 테스트는 메모리까지만, 뷰모델 테스트는 메모리부터만 검사해서 그 사이의 끊김을 놓칠 수 있다.
/// 이 테스트는 실제 TCP 소켓 → 서버 → 코덱 → 메모리 → 뷰모델 Refresh → LED 까지 전 구간을 지난다.
/// </remarks>
public class MasterWriteLightsTheLampTests
{
    private static MainWindowViewModel NewViewModel(params string[] digitalAddresses)
        => new(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
                DigitalPoints = digitalAddresses
                    .Select(a => new DigitalPointEntry { Address = a })
                    .ToArray(),
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    private static byte[] U16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    /// <summary>개별 쓰기 데이터부. <paramref name="withSizeField"/> 로 두 배치를 모두 만든다.</summary>
    private static byte[] WriteRequest(ushort dataType, string name, byte[] value, bool withSizeField)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0058));
        b.AddRange(U16(dataType));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        var ascii = Encoding.ASCII.GetBytes(name);
        b.AddRange(U16((ushort)ascii.Length));
        b.AddRange(ascii);
        if (withSizeField)
        {
            b.AddRange(U16((ushort)value.Length));
        }

        b.AddRange(value);
        return b.ToArray();
    }

    private static byte[] Frame(byte[] data)
    {
        var frame = new byte[20 + data.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        frame[12] = 0xA0;
        frame[13] = 0x33;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
        data.CopyTo(frame, 20);
        return frame;
    }

    /// <summary>서버를 띄우고 프레임 하나를 보낸 뒤 응답을 받아 온다.</summary>
    private static async Task<byte[]> SendAsync(MainWindowViewModel vm, byte[] data)
    {
        await vm.Engine.StartServerAsync();
        var port = vm.Engine.LocalEndPoint!.Port;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        tcp.NoDelay = true;
        var stream = tcp.GetStream();

        await stream.WriteAsync(Frame(data));

        var header = new byte[20];
        await ReadExactlyAsync(stream, header);
        var body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(16))];
        await ReadExactlyAsync(stream, body);

        await vm.Engine.StopServerAsync();
        return [.. header, .. body];
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read));
            if (n == 0)
            {
                throw new IOException("응답 완독 전에 연결이 닫혔습니다");
            }

            read += n;
        }
    }

    /// <summary>응답의 ErrorStatus (데이터부 오프셋 6).</summary>
    private static ushort ErrorStatus(byte[] frame)
        => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20 + 6));

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MasterWordWriteLightsTheMatchingBit(bool withSizeField)
    {
        var vm = NewViewModel("%MW0");
        new MainWindow { DataContext = vm }.Show();

        var group = vm.DigitalGroups[0];
        Assert.Equal(16, group.Bits.Count);
        Assert.All(group.Bits, b => Assert.False(b.IsOn));

        // 값 0x0002 → 비트 1 이 켜져야 한다.
        var response = await SendAsync(
            vm, WriteRequest(0x0002, "%MW000", [0x02, 0x00], withSizeField));

        Assert.Equal(0, ErrorStatus(response));

        vm.Refresh();
        Assert.False(group.Bits[0].IsOn);
        Assert.True(group.Bits[1].IsOn);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task FieldCapture_DwordWriteAtAWordNameLightsBitsInBothWords()
    {
        // 현장에서 관측된 모양: 이름 %MW000(2바이트) + 값 4바이트.
        var vm = NewViewModel("%MW0", "%MW1");
        new MainWindow { DataContext = vm }.Show();

        var response = await SendAsync(
            vm, WriteRequest(0x0003, "%MW000", [0x02, 0x00, 0x01, 0x00], withSizeField: false));

        Assert.Equal(0, ErrorStatus(response));

        vm.Refresh();
        Assert.True(vm.DigitalGroups[0].Bits[1].IsOn);    // %MW000 = 0x0002
        Assert.True(vm.DigitalGroups[1].Bits[0].IsOn);    // %MW001 = 0x0001

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task MasterBitWriteLightsASingleBitPoint()
    {
        var vm = NewViewModel("%MX801");
        new MainWindow { DataContext = vm }.Show();

        var response = await SendAsync(
            vm, WriteRequest(0x0000, "%MX801", [0x01], withSizeField: true));

        Assert.Equal(0, ErrorStatus(response));

        vm.Refresh();
        Assert.True(vm.DigitalGroups[0].Bits[0].IsOn);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task MasterWriteAlsoShowsUpInTheWatchAndAnalogTabs()
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            Watches = [new WatchEntry { Address = "%MW0" }],
            AnalogPoints = [new AnalogPointEntry { Address = "%MW0" }],
        };

        var vm = new MainWindowViewModel(
            project, memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));
        new MainWindow { DataContext = vm }.Show();

        await SendAsync(vm, WriteRequest(0x0002, "%MW000", [0xD2, 0x04], withSizeField: true));

        vm.Refresh();
        Assert.Equal("1234", vm.Watches[0].ValueText);
        Assert.Equal("1234", vm.AnalogPoints[0].RawText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task TheWriteIsRecordedInTheTrafficLogAsAcceptedNotRejected()
    {
        var vm = NewViewModel("%MW0");
        new MainWindow { DataContext = vm }.Show();

        await SendAsync(vm, WriteRequest(0x0003, "%MW000", [0x02, 0x00, 0x00, 0x00], withSizeField: false));

        vm.RefreshTraffic();

        // 거절 행이 하나도 없어야 한다 — 있으면 사유가 그 자리에 적혀 있어야 한다.
        var rejected = vm.TrafficRows.Where(r => r.IsError).ToList();
        Assert.True(
            rejected.Count == 0,
            "거절됨: " + string.Join(" / ", rejected.Select(r => r.SummaryText)));

        vm.Shutdown();
    }
}
