using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 모든 탭을 **실제로 렌더**한다.
/// </summary>
/// <remarks>
/// TabControl 의 탭 내용은 선택될 때 지연 생성된다. 그래서 뷰모델만 조작하는 테스트는
/// 선택되지 않은 탭의 DataTemplate 안 바인딩 오류를 잡지 못한다 —
/// 실제로 워치 탭의 조상 캐스팅 바인딩이 런타임 타입 해석에 실패해 앱을 죽이는 버그가
/// 이 방식의 테스트가 없어 통과해 버렸다(스크린샷 도구가 대신 잡았다).
/// 이 테스트는 탭을 하나씩 선택하고 레이아웃을 강제해 그 부류를 잡는다.
/// </remarks>
public class TabRenderTests
{
    private static MainWindowViewModel BuildPopulatedViewModel()
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            AnalogChannels =
            [
                new AnalogChannelSettings
                {
                    SlotNumber = 5, Channel = 0,
                    Scale = new AnalogChannelScale
                    {
                        RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
                    },
                },
            ],
            AutomationRules =
            [
                new AutomationRuleSettings
                {
                    Address = "%MW300", Kind = GeneratorKind.Ramp,
                    Min = 0, Max = 100, Step = 25, PeriodMs = 200,
                },
            ],
            Watches =
            [
                new WatchEntry { Address = "%MW320", Label = "설정 압력" },
                new WatchEntry { Address = "%MD422", Label = "적산 유량", Format = WatchFormat.Hex },
                new WatchEntry { Address = "%MX801", Label = "운전 지령", Format = WatchFormat.Bool },
                new WatchEntry
                {
                    Address = "%MD500", Label = "유량 (실수)",
                    Format = WatchFormat.Float, Order = ByteOrder.Abcd,
                },
                new WatchEntry { Address = "%ML60", Label = "적산 (배정도)", Format = WatchFormat.Double },
            ],
            AnalogPoints =
            [
                new AnalogPointEntry
                {
                    Address = "%IW80", Label = "탱크 압력",
                    Scale = new AnalogChannelScale
                    {
                        RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "bar",
                    },
                },
            ],
            DigitalPoints =
            [
                new DigitalPointEntry { Address = "%MX900", Label = "운전 지령" },
                new DigitalPointEntry { Address = "%QX2000", Label = "운전 상태" },
            ],
        };

        return new MainWindowViewModel(
            project, memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));
    }

    [AvaloniaFact]
    public void EveryTabRendersWithoutThrowing()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        var tabCount = tabControl.ItemCount;
        Assert.Equal(4, tabCount);

        for (var i = 0; i < tabCount; i++)
        {
            tabControl.SelectedIndex = i;

            // 레이아웃·바인딩을 강제로 흘려 DataTemplate 안까지 실체화한다.
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(window.Width, window.Height));
            window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(tabControl.SelectedItem);
        }

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WatchTabRealisesItsRowsIncludingTheRemoveButton()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 2;   // 주소 워치
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        // 워치 주소가 화면에 실제로 그려졌는지 확인한다.
        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("%MW320", texts);
        Assert.Contains("%MD422", texts);
        Assert.Contains("%ML60", texts);
        Assert.Contains("설정 압력", texts);

        // 제거 버튼이 실체화되고 커맨드가 연결되어 동작해야 한다.
        var removeButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Command is not null && b.Content is PathIcon)
            .ToList();
        Assert.NotEmpty(removeButtons);

        var before = vm.Watches.Count;
        vm.Watches[0].RemoveCommand.Execute(null);
        Assert.Equal(before - 1, vm.Watches.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void MergedDigitalTabRealisesEveryPointRow()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();

        tabControl.SelectedIndex = 0;   // 디지털 I/O
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();

        // 입력·출력이 한 목록에 함께 있다.
        Assert.Contains("%MX900", texts);
        Assert.Contains("%QX2000", texts);
        Assert.Equal(2, vm.DigitalGroups.Count);
        Assert.All(vm.DigitalGroups, g => Assert.True(g.IsWritable));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AnalogTabRealisesItsUserDefinedChannels()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();

        tabControl.SelectedIndex = 1;   // A/D 입력
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("%IW80", texts);
        Assert.Contains("탱크 압력", texts);
        Assert.Single(vm.AnalogPoints);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OnlyTheFourWorkingTabsRemain()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var headers = window.GetVisualDescendants().OfType<TabItem>()
            .Select(t => t.Header?.ToString() ?? string.Empty).ToList();

        Assert.Equal(
            new[] { "디지털 I/O", "A/D 입력", "주소 워치", "트래픽 로그" },
            headers);
        Assert.DoesNotContain("값 자동화", headers);
        Assert.DoesNotContain("랙 구성", headers);
        // 입력/출력은 하나로 합쳐졌다 — 모든 점이 양방향이다.
        Assert.DoesNotContain("디지털 입력", headers);
        Assert.DoesNotContain("디지털 출력", headers);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DraftCodecNoticeIsGone()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.DoesNotContain("미검증 초안 코덱으로 동작 중입니다", texts);
        Assert.False(Nxs.Core.Protocol.Xgt.XgtFenetCodec.IsDraft);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FooterShowsCopyrightAndLogo()
    {
        var vm = BuildPopulatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("© 2026 NEXYS · MIT licensed", texts);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<Image>());

        vm.Shutdown();
    }
}
