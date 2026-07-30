using Avalonia.Headless.XUnit;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Protocol;
using Nxs.TestKit;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>PRD M6 스모크 — 값 자동화 + 트래픽 로그 UI.</summary>
public class AutomationAndTrafficSmokeTests
{
    private static NxpProject ProjectWithRules(params AutomationRuleSettings[] rules)
        => NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            AnalogChannels =
            [
                new AnalogChannelSettings
                {
                    SlotNumber = 5,
                    Channel = 0,
                    Scale = new AnalogChannelScale
                    {
                        RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
                    },
                },
            ],
            AutomationRules = rules,
        };

    private static MainWindowViewModel NewViewModel(NxpProject project)
        => new(project, m => new TestOnlyFrameCodec(new PlcRequestExecutor(m)));

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "대기 조건이 시간 내에 충족되지 않았습니다.");
    }

    [AvaloniaFact]
    public void RulesFromTheProjectAppearInTheAutomationPanel()
    {
        var vm = NewViewModel(ProjectWithRules(
            new AutomationRuleSettings
            {
                Address = "%MW100", Kind = GeneratorKind.Ramp, Min = 0, Max = 100, Step = 25, PeriodMs = 100,
            },
            new AutomationRuleSettings
            {
                Address = "%MX200", Kind = GeneratorKind.Toggle, PeriodMs = 500,
            }));
        new MainWindow { DataContext = vm }.Show();

        Assert.True(vm.HasAutomationRules);
        Assert.Equal(2, vm.AutomationRules.Count);
        Assert.Equal("%MW100", vm.AutomationRules[0].AddressText);
        Assert.Equal("램프", vm.AutomationRules[0].KindText);
        Assert.Equal("100 ms", vm.AutomationRules[0].PeriodText);
        Assert.Equal("토글", vm.AutomationRules[1].KindText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RulePreviewShowsTheGoldenVector()
    {
        var vm = NewViewModel(ProjectWithRules(new AutomationRuleSettings
        {
            Address = "%MW100", Kind = GeneratorKind.Ramp, Min = 0, Max = 100, Step = 25, PeriodMs = 100,
        }));
        new MainWindow { DataContext = vm }.Show();

        Assert.Equal("0, 25, 50, 75, 100, 0, 25, 50", vm.AutomationRules[0].PreviewText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void EngineeringUnitRuleShowsTheSharedChannelScale()
    {
        var vm = NewViewModel(ProjectWithRules(new AutomationRuleSettings
        {
            Address = "%IW80",
            Kind = GeneratorKind.Ramp,
            Min = 0, Max = 10, Step = 5, PeriodMs = 100,
            UseEngineeringUnits = true,
        }));
        new MainWindow { DataContext = vm }.Show();

        Assert.Contains("V", vm.AutomationRules[0].ScaleText, StringComparison.Ordinal);
        Assert.Contains("raw 0~4000", vm.AutomationRules[0].ScaleText, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task StartingAutomationDrivesMemoryAndTheUiFollows()
    {
        var vm = NewViewModel(ProjectWithRules(new AutomationRuleSettings
        {
            Address = "%IW80",
            Kind = GeneratorKind.Fixed,
            Min = 1234, Max = 1234, PeriodMs = 20,
            UseEngineeringUnits = false,
        }));
        new MainWindow { DataContext = vm }.Show();

        await vm.ToggleAutomationCommand.ExecuteAsync(null);
        Assert.True(vm.IsAutomationRunning);

        var channel = vm.AnalogSlots[0].Channels[0];
        await WaitUntilAsync(() =>
        {
            vm.Refresh();
            return channel.RawText == "1234";
        });

        Assert.Equal("1234", channel.RawText);

        await vm.ToggleAutomationCommand.ExecuteAsync(null);
        Assert.False(vm.IsAutomationRunning);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task DisablingARuleInTheUiStopsItDriving()
    {
        var vm = NewViewModel(ProjectWithRules(new AutomationRuleSettings
        {
            Address = "%MW300", Kind = GeneratorKind.Increment, Min = 0, Max = 9999, Step = 1, PeriodMs = 20,
        }));
        new MainWindow { DataContext = vm }.Show();

        await vm.ToggleAutomationCommand.ExecuteAsync(null);
        var address = Core.Memory.IecAddress.Parse("%MW300");
        await WaitUntilAsync(() => vm.Engine.Memory.ReadScalar(address) > 2);

        vm.AutomationRules[0].IsEnabled = false;
        await Task.Delay(80);
        var frozen = vm.Engine.Memory.ReadScalar(address);
        await Task.Delay(120);

        Assert.Equal(frozen, vm.Engine.Memory.ReadScalar(address));

        await vm.ToggleAutomationCommand.ExecuteAsync(null);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task StartingAutomationWithNoRulesReportsItInsteadOfSilentlyDoingNothing()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();

        await vm.ToggleAutomationCommand.ExecuteAsync(null);

        Assert.False(vm.IsAutomationRunning);
        Assert.Contains("자동화 룰이 없습니다", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ServerTrafficAppearsInTheLogWithHexAndSummary()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();
        await vm.ToggleServerCommand.ExecuteAsync(null);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        await client.ReadIndividualAsync("%MW10");

        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Any(r => r.DirectionText == "RX")
                && vm.TrafficRows.Any(r => r.DirectionText == "TX");
        });

        var rx = vm.TrafficRows.First(r => r.DirectionText == "RX");
        Assert.Contains("%MW10", rx.SummaryText, StringComparison.Ordinal);
        Assert.NotEmpty(rx.HexText);
        Assert.False(rx.IsError);
        Assert.Empty(rx.ReasonText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task RejectedRequestShowsAsAnErrorRowWithItsReason()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();
        await vm.ToggleServerCommand.ExecuteAsync(null);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        await client.ReadContinuousAsync("%MB65530", 16);

        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Any(r => r.IsError);
        });

        var error = vm.TrafficRows.First(r => r.IsError);
        Assert.Equal("RangeExceeded", error.ReasonText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ErrorsOnlyFilterHidesNormalRows()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();
        await vm.ToggleServerCommand.ExecuteAsync(null);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        await client.ReadIndividualAsync("%MW0");
        await client.ReadContinuousAsync("%MB65530", 16);

        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Any(r => r.IsError);
        });

        vm.ShowErrorsOnly = true;
        vm.RefreshTraffic();

        Assert.NotEmpty(vm.TrafficRows);
        Assert.All(vm.TrafficRows, r => Assert.True(r.IsError));

        vm.ShowErrorsOnly = false;
        vm.RefreshTraffic();
        Assert.Contains(vm.TrafficRows, r => !r.IsError);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task PausingFreezesTheDisplayButKeepsCollecting()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();
        await vm.ToggleServerCommand.ExecuteAsync(null);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        await client.ReadIndividualAsync("%MW0");
        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Count > 0;
        });

        vm.IsTrafficPaused = true;
        var frozenCount = vm.TrafficRows.Count;

        for (var i = 0; i < 3; i++)
        {
            await client.ReadIndividualAsync("%MW1");
        }

        vm.RefreshTraffic();
        Assert.Equal(frozenCount, vm.TrafficRows.Count);

        // 수집은 계속되었으므로 재개하면 늘어난다.
        vm.IsTrafficPaused = false;
        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Count > frozenCount;
        });

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ClearEmptiesTheLog()
    {
        var vm = NewViewModel(ProjectWithRules());
        new MainWindow { DataContext = vm }.Show();
        await vm.ToggleServerCommand.ExecuteAsync(null);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        await client.ReadIndividualAsync("%MW0");
        await WaitUntilAsync(() =>
        {
            vm.RefreshTraffic();
            return vm.TrafficRows.Count > 0;
        });

        vm.ClearTrafficCommand.Execute(null);

        Assert.Empty(vm.TrafficRows);
        Assert.Equal(0, vm.TrafficLog.Count);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task TrafficLogSavesToFile()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-ui-log-").FullName;
        try
        {
            var vm = NewViewModel(ProjectWithRules());
            new MainWindow { DataContext = vm }.Show();
            await vm.ToggleServerCommand.ExecuteAsync(null);

            await using var client = await PlcTestClient.ConnectAsync(
                "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
            await client.ReadIndividualAsync("%MW10");
            await WaitUntilAsync(() =>
            {
                vm.RefreshTraffic();
                return vm.TrafficRows.Count > 0;
            });

            var path = Path.Combine(dir, "traffic.log");
            vm.SaveTraffic(path);

            Assert.Null(vm.ErrorMessage);
            var text = File.ReadAllText(path);
            Assert.Contains("%MW10", text, StringComparison.Ordinal);
            Assert.Contains("RX", text, StringComparison.Ordinal);
            vm.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AutomationRulesSurviveProjectSaveAndReopen()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-ui-rules-").FullName;
        try
        {
            var path = Path.Combine(dir, "rules.nxp");
            var vm = NewViewModel(ProjectWithRules(new AutomationRuleSettings
            {
                Address = "%MW100", Kind = GeneratorKind.Sine, Min = 0, Max = 1000, Period = 4, PeriodMs = 250,
            }));
            new MainWindow { DataContext = vm }.Show();

            vm.AutomationRules[0].IsEnabled = false;
            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel(ProjectWithRules());
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            var rule = Assert.Single(reopened.AutomationRules);
            Assert.Equal("%MW100", rule.AddressText);
            Assert.Equal("사인", rule.KindText);
            Assert.Equal("250 ms", rule.PeriodText);
            Assert.False(rule.IsEnabled);
            reopened.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
