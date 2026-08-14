using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Configuration;
using Nxs.Core.Diagnostics;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 트래픽 로그 — 정렬 방향과 프레임 전문 해부 패널.
/// </summary>
public class FrameAnatomyPanelTests
{
    private static byte[] U16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    /// <summary>개별 읽기 요청 프레임 하나.</summary>
    private static byte[] ReadFrame(params string[] names)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0054));
        body.AddRange(U16(0x0002));
        body.AddRange(U16(0));
        body.AddRange(U16((ushort)names.Length));
        foreach (var n in names)
        {
            var a = Encoding.ASCII.GetBytes(n);
            body.AddRange(U16((ushort)a.Length));
            body.AddRange(a);
        }

        var data = body.ToArray();
        var frame = new byte[20 + data.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        frame[13] = 0x33;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
        data.CopyTo(frame, 20);
        return frame;
    }

    private static TrafficEvent Event(byte[] raw, TrafficDirection direction, string summary,
        params string[] addresses) => new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Direction = direction,
            ClientId = "127.0.0.1:5000",
            Raw = raw,
            Summary = summary,
            Addresses = addresses,
        };

    private static MainWindowViewModel Build(TrafficLog log)
    {
        var vm = new MainWindowViewModel(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
            log);
        vm.RefreshTraffic();
        return vm;
    }

    private static TrafficLog ThreeRows()
    {
        var log = new TrafficLog();
        log.Record(Event(ReadFrame("%MD310"), TrafficDirection.Rx, "첫 번째", "%MD310"));
        log.Record(Event(ReadFrame("%MD311"), TrafficDirection.Rx, "두 번째", "%MD311"));
        log.Record(Event(ReadFrame("%MD312"), TrafficDirection.Rx, "세 번째", "%MD312"));
        return log;
    }

    // ==================== 정렬 ====================

    [AvaloniaFact]
    public void NewestIsOnTopByDefault()
    {
        var vm = Build(ThreeRows());

        Assert.True(vm.NewestTrafficFirst);
        Assert.Equal("세 번째", vm.TrafficRows[0].SummaryText);
        Assert.Equal("첫 번째", vm.TrafficRows[^1].SummaryText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheSortButtonFlipsTheOrderBothWays()
    {
        var vm = Build(ThreeRows());

        vm.ToggleTrafficSortCommand.Execute(null);
        Assert.False(vm.NewestTrafficFirst);
        Assert.Equal("첫 번째", vm.TrafficRows[0].SummaryText);

        vm.ToggleTrafficSortCommand.Execute(null);
        Assert.True(vm.NewestTrafficFirst);
        Assert.Equal("세 번째", vm.TrafficRows[0].SummaryText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheSortButtonLabelSaysWhichWayItIs()
    {
        var vm = Build(ThreeRows());

        Assert.Contains("위로", vm.TrafficSortLabel, StringComparison.Ordinal);
        vm.ToggleTrafficSortCommand.Execute(null);
        Assert.Contains("아래로", vm.TrafficSortLabel, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheDisplayCapKeepsTheNewestRowsWhicheverWayItIsSorted()
    {
        var log = new TrafficLog();
        for (var i = 0; i < 600; i++)
        {
            log.Record(Event(ReadFrame("%MW0"), TrafficDirection.Rx, $"#{i}"));
        }

        var vm = Build(log);

        // 상한(500)에 걸려도 잘려 나가는 쪽은 오래된 것이어야 한다.
        Assert.Equal(500, vm.TrafficRows.Count);
        Assert.Equal("#599", vm.TrafficRows[0].SummaryText);
        Assert.Equal("#100", vm.TrafficRows[^1].SummaryText);

        vm.ToggleTrafficSortCommand.Execute(null);
        Assert.Equal("#100", vm.TrafficRows[0].SummaryText);
        Assert.Equal("#599", vm.TrafficRows[^1].SummaryText);

        vm.Shutdown();
    }

    // ==================== 프레임 해부 ====================

    [AvaloniaFact]
    public void SelectingARowBreaksItsFrameIntoNamedFields()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        Assert.NotEmpty(vm.FrameFields);
        Assert.Equal(vm.TrafficRows[0].Source.Raw.Length, vm.FrameBytes.Count);

        // 헤더가 어디까지인지 알 수 있어야 한다.
        var header = vm.FrameFields.Where(f => f.IsHeader).ToList();
        Assert.Equal(0, header[0].Field.Offset);
        Assert.Equal(20, header[^1].Field.End);
        Assert.All(vm.FrameBytes.Take(20), b => Assert.True(b.IsHeader));
        Assert.All(vm.FrameBytes.Skip(20), b => Assert.False(b.IsHeader));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ClickingAnAddressBlocksOutItsBytesInTheFrame()
    {
        var log = new TrafficLog();
        log.Record(Event(ReadFrame("%MD310", "%MD311", "%MD312"), TrafficDirection.Rx, "읽기",
            "%MD310", "%MD311", "%MD312"));
        var vm = Build(log);
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        Assert.Equal(["%MD310", "%MD311", "%MD312"], vm.FrameAddresses);

        vm.SelectFrameAddressCommand.Execute("%MD312");

        var picked = vm.FrameBytes.Where(b => b.IsHighlighted).ToList();
        Assert.Equal(6, picked.Count);   // "%MD312" 6글자
        Assert.Equal(
            "%MD312",
            Encoding.ASCII.GetString(picked.Select(b => b.Value).ToArray()));
        Assert.Contains("%MD312", vm.SelectedFrameFieldText, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ForAWriteTheAddressPicksTheValueBytesNotJustTheName()
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0058));
        body.AddRange(U16(0x0002));
        body.AddRange(U16(0));
        body.AddRange(U16(1));
        var ascii = Encoding.ASCII.GetBytes("%MW10");
        body.AddRange(U16((ushort)ascii.Length));
        body.AddRange(ascii);
        body.AddRange(U16(2));
        body.AddRange([0x34, 0x12]);

        var data = body.ToArray();
        var frame = new byte[20 + data.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        frame[13] = 0x33;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
        data.CopyTo(frame, 20);

        var log = new TrafficLog();
        log.Record(Event(frame, TrafficDirection.Rx, "쓰기", "%MW10"));
        var vm = Build(log);
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        vm.SelectFrameAddressCommand.Execute("%MW10");

        // 값이 있으면 값을 보고 싶은 것이 보통이다.
        var picked = vm.FrameBytes.Where(b => b.IsHighlighted).ToList();
        Assert.Equal([0x34, 0x12], picked.Select(b => b.Value));
        Assert.True(vm.SelectedFrameField!.IsValue);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ClickingAByteSelectsTheFieldThatContainsIt()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        // 12번 바이트는 CPU 정보다.
        vm.SelectFrameByteCommand.Execute(vm.FrameBytes[12]);

        Assert.Equal("CPU 정보", vm.SelectedFrameField!.Name);
        var picked = Assert.Single(vm.FrameBytes, b => b.IsHighlighted);
        Assert.Equal(12, picked.Offset);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SelectingAFieldFromTheListHighlightsExactlyItsBytes()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        var company = vm.FrameFields.First(f => f.Name == "회사 ID");
        vm.SelectFrameFieldCommand.Execute(company);

        Assert.True(company.IsSelected);
        Assert.Equal(
            Enumerable.Range(0, 10),
            vm.FrameBytes.Where(b => b.IsHighlighted).Select(b => b.Offset));

        // 다른 구간을 고르면 앞의 선택은 풀린다.
        var bcc = vm.FrameFields.First(f => f.Name == "BCC");
        vm.SelectFrameFieldCommand.Execute(bcc);
        Assert.False(company.IsSelected);
        Assert.Equal(19, Assert.Single(vm.FrameBytes, b => b.IsHighlighted).Offset);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChangingTheSelectedRowResetsTheAnatomy()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];
        vm.SelectFrameByteCommand.Execute(vm.FrameBytes[0]);
        Assert.NotNull(vm.SelectedFrameField);

        vm.SelectedTrafficRow = vm.TrafficRows[1];

        Assert.Null(vm.SelectedFrameField);
        Assert.DoesNotContain(vm.FrameBytes, b => b.IsHighlighted);
        Assert.Equal(["%MD311"], vm.FrameAddresses);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ClearingTheSelectionEmptiesTheAnatomy()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];
        Assert.NotEmpty(vm.FrameBytes);

        vm.SelectedTrafficRow = null;

        Assert.Empty(vm.FrameBytes);
        Assert.Empty(vm.FrameFields);
        Assert.Empty(vm.FrameAddresses);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ARowWithNoFrameBytesShowsNoAnatomyRatherThanCrashing()
    {
        var log = new TrafficLog();
        log.Record(new TrafficEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Direction = TrafficDirection.Note,
            ClientId = "-",
            Summary = "수신 시작",
        });

        var vm = Build(log);
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        Assert.Empty(vm.FrameBytes);
        Assert.Empty(vm.FrameFields);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void EveryByteBelongsToExactlyOneField()
    {
        var vm = Build(ThreeRows());
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        // 어느 바이트를 눌러도 구간이 잡혀야 한다 — 빈틈이 있으면 진단이 막힌다.
        Assert.All(vm.FrameBytes, b => Assert.True(b.FieldIndex >= 0, $"바이트 {b.Offset} 가 어디에도 속하지 않는다"));

        foreach (var cell in vm.FrameBytes)
        {
            Assert.True(vm.FrameFields[cell.FieldIndex].Field.Contains(cell.Offset));
        }

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheTrafficTabRendersTheByteGridAndTheFieldList()
    {
        var vm = Build(ThreeRows());
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 900 };
        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 5;   // 트래픽 로그
        vm.SelectedTrafficRow = vm.TrafficRows[0];

        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var byteButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is FrameByteViewModel).ToList();
        var fieldButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is FrameFieldViewModel).ToList();

        Assert.Equal(vm.FrameBytes.Count, byteButtons.Count);
        Assert.Equal(vm.FrameFields.Count, fieldButtons.Count);

        // 바이트를 실제로 눌러 보면 그 구간이 잡혀야 한다.
        var target = byteButtons.Single(b => ((FrameByteViewModel)b.DataContext!).Offset == 13);
        target.Command!.Execute(target.CommandParameter);
        Assert.Equal("방향", vm.SelectedFrameField!.Name);

        vm.Shutdown();
    }
}

/// <summary>범위 보기 칸이 형식에 맞는 너비를 갖는지 — 2진 표기가 잘리던 문제.</summary>
public class RangeCellWidthTests
{
    [Theory]
    [InlineData("%MW0", WatchFormat.Binary)]
    [InlineData("%MD0", WatchFormat.Binary)]
    [InlineData("%ML0", WatchFormat.Binary)]
    [InlineData("%MW0", WatchFormat.Hex)]
    [InlineData("%ML0", WatchFormat.Decimal)]
    [InlineData("%MD0", WatchFormat.Float)]
    [InlineData("%ML0", WatchFormat.Double)]
    public void TheCellIsWideEnoughForTheLongestTextThatFormatCanProduce(string address, WatchFormat format)
    {
        var parsed = IecAddress.Parse(address);
        var width = RangeCellViewModel.WidthFor(format, parsed);

        // 최악 표기가 들어갈 만큼은 되어야 한다 — 잘리면 화면이 쓸모없어진다.
        var maxChars = WatchValue.MaxRenderedLength(format, parsed.ByteLength);
        Assert.True(width >= (maxChars * 7.4) + 20 - 1, $"{address} · {format} 칸이 좁다 ({width})");
    }

    [Fact]
    public void BinaryIsWiderThanHexWhichIsWiderThanBool()
    {
        var word = IecAddress.Parse("%MW0");

        Assert.True(
            RangeCellViewModel.WidthFor(WatchFormat.Binary, word)
            > RangeCellViewModel.WidthFor(WatchFormat.Hex, word));
        Assert.True(
            RangeCellViewModel.WidthFor(WatchFormat.Hex, word)
            >= RangeCellViewModel.WidthFor(WatchFormat.Bool, word));
    }

    [Fact]
    public void EveryCellInARangeGetsTheSameWidthSoTheGridStaysTidy()
    {
        var vm = new MainWindowViewModel(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Binary);
        vm.RangeStartAddress = "%MW0";
        vm.UseRangeCountCommand.Execute(20);

        var widths = vm.RangeCells.Select(c => c.CellWidth).Distinct().ToList();
        Assert.Single(widths);

        vm.Shutdown();
    }

    [Fact]
    public void ALongAddressStillFitsEvenInANarrowFormat()
    {
        // "%MW65535" 8글자 — Bool("ON") 형식이라도 주소가 잘리면 안 된다.
        var width = RangeCellViewModel.WidthFor(WatchFormat.Bool, IecAddress.Parse("%MW65535"));
        Assert.True(width >= 96);
    }
}
