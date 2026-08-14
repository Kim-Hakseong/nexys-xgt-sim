using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Configuration;
using Nxs.Core.Diagnostics;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 트래픽 로그 필터 UI — 방향 3택(RX+TX / RX만 / TX만) + 주소 필터.
/// </summary>
/// <remarks>
/// 뷰모델만 검사하면 지연 생성되는 탭 안 바인딩 오류를 놓친다(TabRenderTests 주석 참조).
/// 그래서 실제로 트래픽 탭을 선택해 레이아웃까지 흘린다.
/// </remarks>
public class TrafficFilterUiTests
{
    private static TrafficEvent Rx(string summary, params string[] addresses) => new()
    {
        Timestamp = DateTimeOffset.UnixEpoch,
        Direction = TrafficDirection.Rx,
        ClientId = "127.0.0.1:5000",
        Raw = [0x4C, 0x53, 0x49, 0x53],
        Summary = summary,
        Addresses = addresses,
    };

    private static TrafficEvent Tx(string summary, params string[] addresses) => new()
    {
        Timestamp = DateTimeOffset.UnixEpoch,
        Direction = TrafficDirection.Tx,
        ClientId = "127.0.0.1:5000",
        Raw = [0x4C, 0x53, 0x49, 0x53],
        Summary = summary,
        Addresses = addresses,
    };

    private static (MainWindowViewModel Vm, TrafficLog Log) Build()
    {
        var log = new TrafficLog();
        log.Record(Rx("읽기 요청 %MW320", "%MW320"));
        log.Record(Tx("읽기 응답 %MW320", "%MW320"));
        log.Record(Rx("쓰기 요청 %MD422", "%MD422"));
        log.Record(Tx("쓰기 응답 %MD422", "%MD422"));

        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        };

        var vm = new MainWindowViewModel(
            project,
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
            log);
        vm.RefreshTraffic();
        return (vm, log);
    }

    [AvaloniaFact]
    public void DefaultDirectionShowsRxAndTxTogether()
    {
        var (vm, _) = Build();

        Assert.Equal(TrafficDirectionFilter.RxAndTx, vm.SelectedDirectionOption.Value);
        Assert.Equal(4, vm.TrafficRows.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ThreeDirectionOptionsAreOfferedWithKoreanLabels()
    {
        var (vm, _) = Build();

        Assert.Equal(3, vm.DirectionOptions.Count);
        Assert.Equal(
            new[] { "RX + TX 함께", "RX 만 (마스터 → 시뮬)", "TX 만 (시뮬 → 마스터)" },
            vm.DirectionOptions.Select(o => o.Label).ToArray());

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData(TrafficDirectionFilter.RxAndTx, 4)]
    [InlineData(TrafficDirectionFilter.RxOnly, 2)]
    [InlineData(TrafficDirectionFilter.TxOnly, 2)]
    public void ChangingDirectionRefiltersImmediately(TrafficDirectionFilter direction, int expected)
    {
        var (vm, _) = Build();

        vm.SelectedDirectionOption = vm.DirectionOptions.Single(o => o.Value == direction);

        Assert.Equal(expected, vm.TrafficRows.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AddingAnAddressNarrowsTheRowsAndRemovingItRestoresThem()
    {
        var (vm, _) = Build();

        vm.NewTrafficAddress = "%MW320";
        vm.AddTrafficAddressCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "%MW320" }, vm.TrafficAddresses);
        Assert.True(vm.HasTrafficAddressFilter);
        Assert.Equal(2, vm.TrafficRows.Count);
        Assert.All(vm.TrafficRows, r => Assert.Equal("%MW320", r.AddressText));

        vm.RemoveTrafficAddressCommand.Execute("%MW320");

        Assert.False(vm.HasTrafficAddressFilter);
        Assert.Equal(4, vm.TrafficRows.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AddressFilterCombinesWithDirection()
    {
        var (vm, _) = Build();

        vm.NewTrafficAddress = "%MD422";
        vm.AddTrafficAddressCommand.Execute(null);
        vm.SelectedDirectionOption =
            vm.DirectionOptions.Single(o => o.Value == TrafficDirectionFilter.TxOnly);

        var row = Assert.Single(vm.TrafficRows);
        Assert.Equal("TX", row.DirectionText);
        Assert.Equal("%MD422", row.AddressText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SeveralAddressesAreOredTogether()
    {
        var (vm, _) = Build();

        vm.NewTrafficAddress = "%MW320";
        vm.AddTrafficAddressCommand.Execute(null);
        vm.NewTrafficAddress = "%MD422";
        vm.AddTrafficAddressCommand.Execute(null);

        Assert.Equal(2, vm.TrafficAddresses.Count);
        Assert.Equal(4, vm.TrafficRows.Count);

        vm.ClearTrafficAddressesCommand.Execute(null);
        Assert.Empty(vm.TrafficAddresses);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FullWidthTypedAddressIsNormalisedLikeTheOtherInputs()
    {
        var (vm, _) = Build();

        // 한글 IME 로 입력하면 전각 '％' 가 섞인다 — 다른 추가 입력과 같은 경로를 타야 한다.
        vm.NewTrafficAddress = "％ｍｗ３２０";
        vm.AddTrafficAddressCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "%MW320" }, vm.TrafficAddresses);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void UnparsableAddressReportsWhyAndAddsNothing()
    {
        var (vm, _) = Build();

        vm.NewTrafficAddress = "MW";
        vm.AddTrafficAddressCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.TrafficAddresses);
        Assert.Equal(4, vm.TrafficRows.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DuplicateAddressIsRejectedRatherThanListedTwice()
    {
        var (vm, _) = Build();

        vm.NewTrafficAddress = "%MW320";
        vm.AddTrafficAddressCommand.Execute(null);
        vm.NewTrafficAddress = "%mw320";
        vm.AddTrafficAddressCommand.Execute(null);

        Assert.Single(vm.TrafficAddresses);
        Assert.NotNull(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ErrorsOnlyStillCombinesWithTheNewFilters()
    {
        var log = new TrafficLog();
        log.Record(Rx("정상 요청 %MW320", "%MW320"));
        log.Record(new TrafficEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Direction = TrafficDirection.Rx,
            ClientId = "127.0.0.1:5000",
            Raw = [0x00],
            Summary = "잘못된 프레임 %MW320",
            Reason = PlcErrorReason.InvalidAddress,
            Addresses = ["%MW320"],
        });

        var vm = new MainWindowViewModel(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
            log);

        vm.ShowErrorsOnly = true;
        var row = Assert.Single(vm.TrafficRows);
        Assert.Equal("InvalidAddress", row.ReasonText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SelectingARowShowsItsFullFrameForDiagnosis()
    {
        var (vm, _) = Build();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasSelectedTrafficRow);

        vm.SelectedTrafficRow = vm.TrafficRows[0];
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasSelectedTrafficRow);

        // 표의 raw hex 열은 잘리므로 전문이 따로 보여야 한다 — 그래야 진단에 쓸 수 있다.
        var blocks = window.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();
        var texts = blocks.Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(vm.TrafficRows[0].HexText, texts);
        Assert.Contains(vm.TrafficRows[0].SummaryText, texts);

        // 창 안에 실제로 들어와 있어야 한다 — 레이아웃 밖으로 밀려나면 보이지 않는 기능이다.
        foreach (var block in blocks)
        {
            var placed = block.GetTransformedBounds();
            Assert.NotNull(placed);
            Assert.True(placed!.Value.Bounds.Height > 0, "전문 줄의 높이가 0이면 보이지 않는다");
            Assert.InRange(placed.Value.Clip.Top, 0, window.Height);
        }

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RefreshKeepsTheSelectedRowSoThePanelDoesNotCloseWhileReading()
    {
        var (vm, _) = Build();
        vm.SelectedTrafficRow = vm.TrafficRows[1];
        var chosen = vm.TrafficRows[1].Source;

        // 200ms 주기 갱신이 선택을 놓으면 전문을 읽는 도중에 패널이 닫힌다.
        vm.RefreshTraffic();

        Assert.NotNull(vm.SelectedTrafficRow);
        Assert.Same(chosen, vm.SelectedTrafficRow!.Source);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DetailHeaderNamesTheDirectionAddressAndReason()
    {
        var log = new TrafficLog();
        log.Record(new TrafficEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Direction = TrafficDirection.Tx,
            ClientId = "127.0.0.1:5000",
            Raw = [0x00],
            Summary = "거절 · DataSizeMismatch — %MW000 에 쓸 값이 비어 있습니다",
            Reason = PlcErrorReason.DataSizeMismatch,
            Addresses = ["%MW000"],
        });

        var vm = new MainWindowViewModel(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
            log);
        vm.RefreshTraffic();

        var header = vm.TrafficRows[0].DetailHeaderText;
        Assert.Contains("TX", header, StringComparison.Ordinal);
        Assert.Contains("%MW000", header, StringComparison.Ordinal);
        Assert.Contains("DataSizeMismatch", header, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TrafficTabRendersTheDirectionComboAndAddressChips()
    {
        var (vm, _) = Build();
        vm.NewTrafficAddress = "%MW320";
        vm.AddTrafficAddressCommand.Execute(null);

        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 3;   // 트래픽 로그
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "TrafficDirectionBox");
        Assert.Equal(3, combo.ItemCount);
        Assert.Same(vm.SelectedDirectionOption, combo.SelectedItem);

        // 주소 칩이 실제로 그려지고, 그 안의 제거 버튼 커맨드가 연결되어 있어야 한다.
        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("%MW320", texts);

        var chipRemove = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content as string == "✕" && b.Command is not null)
            .ToList();
        Assert.NotEmpty(chipRemove);

        chipRemove[0].Command!.Execute(chipRemove[0].CommandParameter);
        Assert.Empty(vm.TrafficAddresses);

        vm.Shutdown();
    }
}
