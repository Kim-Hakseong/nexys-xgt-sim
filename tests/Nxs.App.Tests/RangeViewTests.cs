using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Nxs.TestKit;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 범위 보기 탭 — 시작 주소 + 개수로 한꺼번에 펼치고, 방금 바뀐 칸을 표시한다.
/// </summary>
/// <remarks>
/// 이 화면의 목적은 "마스터가 어느 주소를 건드리는지 모를 때 찾는 것"이다.
/// 그래서 값을 늘어놓는 것보다 **변경 표시**가 제대로 켜지고 꺼지는지가 핵심이다.
/// </remarks>
public class RangeViewTests
{
    private static MainWindowViewModel NewViewModel(FakeTimeSource? time = null)
        => new(
            NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
            timeSource: time);

    [AvaloniaFact]
    public void ExpandingCreatesOneCellPerAddress()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeStartAddress = "%MW0";
        vm.RangeCountText = "100";
        vm.ExpandRangeCommand.Execute(null);

        Assert.Equal(100, vm.RangeCells.Count);
        Assert.True(vm.HasRangeCells);
        Assert.Equal("%MW0", vm.RangeCells[0].AddressText);
        Assert.Equal("%MW99", vm.RangeCells[^1].AddressText);
        Assert.Contains("%MW0", vm.RangeNotice, StringComparison.Ordinal);
        Assert.Contains("%MW99", vm.RangeNotice, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(500)]
    [InlineData(1000)]
    public void EveryQuickCountButtonExpandsImmediately(int count)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Assert.Contains(count, vm.RangeCountPresets);
        vm.UseRangeCountCommand.Execute(count);

        Assert.Equal(count, vm.RangeCells.Count);
        Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), vm.RangeCountText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ACustomCountIsAcceptedToo()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeCountText = "37";
        vm.ExpandRangeCommand.Execute(null);

        Assert.Equal(37, vm.RangeCells.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AMasterWriteLightsUpExactlyTheCellItLandedOn()
    {
        var time = new FakeTimeSource(DateTimeOffset.UnixEpoch);
        var vm = NewViewModel(time);
        new MainWindow { DataContext = vm }.Show();

        vm.UseRangeCountCommand.Execute(100);
        Assert.All(vm.RangeCells, c => Assert.False(c.IsRecentlyChanged));

        // 마스터가 %MW42 를 건드렸다고 하자.
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW42"), 0x1234);
        vm.Refresh();

        var lit = vm.RangeCells.Where(c => c.IsRecentlyChanged).ToList();
        var only = Assert.Single(lit);
        Assert.Equal("%MW42", only.AddressText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheChangeMarkFadesAfterItsWindowSoTheScreenDoesNotStayLit()
    {
        var time = new FakeTimeSource(DateTimeOffset.UnixEpoch);
        var vm = NewViewModel(time);
        new MainWindow { DataContext = vm }.Show();

        vm.UseRangeCountCommand.Execute(10);
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW3"), 7);
        vm.Refresh();
        Assert.True(vm.RangeCells[3].IsRecentlyChanged);

        // 표시 시간이 지나기 전에는 남아 있어야 한다.
        time.Advance(RangeCellViewModel.ChangeHighlight - TimeSpan.FromMilliseconds(1));
        vm.Refresh();
        Assert.True(vm.RangeCells[3].IsRecentlyChanged);

        time.Advance(TimeSpan.FromMilliseconds(2));
        vm.Refresh();
        Assert.False(vm.RangeCells[3].IsRecentlyChanged);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChangingFormatReprojectsEveryCellWithoutTouchingMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW1"), 4660);

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Decimal);
        vm.UseRangeCountCommand.Execute(10);
        Assert.Equal("4660", vm.RangeCells[1].ValueText);

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Hex);
        Assert.Equal("0x1234", vm.RangeCells[1].ValueText);

        // 형식만 바꿨을 뿐이므로 메모리는 그대로여야 한다.
        Assert.Equal(4660u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW1")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChangingByteOrderReprojectsTheCellsToo()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 0x1234);

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Hex);
        vm.UseRangeCountCommand.Execute(10);
        var little = vm.RangeCells[0].ValueText;

        vm.RangeOrderOption = vm.RangeOrderOptions.Single(o => o.Value == ByteOrder.Abcd);
        Assert.NotEqual(little, vm.RangeCells[0].ValueText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SelectingACellLetsYouWriteItAndTheWriteReachesMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Decimal);
        vm.UseRangeCountCommand.Execute(10);

        Assert.False(vm.HasSelectedRangeCell);
        vm.SelectedRangeCell = vm.RangeCells[5];
        Assert.True(vm.HasSelectedRangeCell);

        vm.SelectedRangeValueText = "1234";
        vm.WriteSelectedRangeCellCommand.Execute(null);

        Assert.Equal(1234u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW5")));
        Assert.Equal("1234", vm.RangeCells[5].ValueText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AnUnparsableWriteIsReportedAndLeavesMemoryAlone()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeFormatOption = vm.RangeFormatOptions.Single(o => o.Value == WatchFormat.Decimal);
        vm.UseRangeCountCommand.Execute(10);
        vm.SelectedRangeCell = vm.RangeCells[5];
        vm.SelectedRangeValueText = "이건 숫자가 아니다";
        vm.WriteSelectedRangeCellCommand.Execute(null);

        Assert.Equal(0u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW5")));
        Assert.Contains("%MW5", vm.RangeNotice, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ACellCanBeMovedIntoTheWatchListToKeepAnEyeOnIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.UseRangeCountCommand.Execute(10);
        vm.SelectedRangeCell = vm.RangeCells[7];
        vm.AddSelectedRangeCellToWatchCommand.Execute(null);

        var watch = Assert.Single(vm.Watches);
        Assert.Equal("%MW7", watch.AddressText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FullWidthTypedStartAddressIsNormalisedLikeEverywhereElse()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeStartAddress = "％ｍｗ１０";
        vm.RangeCountText = "5";
        vm.ExpandRangeCommand.Execute(null);

        Assert.Equal("%MW10", vm.RangeStartAddress);
        Assert.Equal(5, vm.RangeCells.Count);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("MW", "100")]
    [InlineData("%MW0", "0")]
    [InlineData("%MW0", "999999")]
    [InlineData("%MW0", "많이")]
    public void BadInputExplainsItselfAndLeavesTheViewEmpty(string start, string count)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeStartAddress = start;
        vm.RangeCountText = count;
        vm.ExpandRangeCommand.Execute(null);

        Assert.Empty(vm.RangeCells);
        Assert.False(vm.HasRangeCells);
        Assert.NotEmpty(vm.RangeNotice);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ARangeRunningPastMemoryIsRefusedWithTheReason()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.RangeStartAddress = "%MW65000";
        vm.RangeCountText = "1000";
        vm.ExpandRangeCommand.Execute(null);

        Assert.Empty(vm.RangeCells);
        Assert.Contains("메모리", vm.RangeNotice, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ClearingEmptiesTheViewAndDropsTheSelection()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.UseRangeCountCommand.Execute(10);
        vm.SelectedRangeCell = vm.RangeCells[0];

        vm.ClearRangeCommand.Execute(null);

        Assert.Empty(vm.RangeCells);
        Assert.False(vm.HasRangeCells);
        Assert.False(vm.HasSelectedRangeCell);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RangeTabRendersItsCellsAndQuickCountButtons()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();

        vm.UseRangeCountCommand.Execute(10);

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 3;   // 범위 보기
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("%MW0", texts);
        Assert.Contains("%MW9", texts);

        // 빠른 개수 버튼이 모두 실체화되고 커맨드가 연결되어야 한다.
        var buttons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content is int)
            .ToList();
        Assert.Equal(vm.RangeCountPresets.Count, buttons.Count);
        Assert.All(buttons, b => Assert.NotNull(b.Command));

        buttons.Single(b => (int)b.Content! == 300).Command!.Execute(300);
        Assert.Equal(300, vm.RangeCells.Count);

        vm.Shutdown();
    }
}
