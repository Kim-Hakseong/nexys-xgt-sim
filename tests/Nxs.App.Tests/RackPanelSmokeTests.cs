using Avalonia.Headless.XUnit;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.TestKit;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// PRD M5 DoD 스모크 — "테스트 클라이언트로 쓴 값이 LED에, 토글이 읽기에 반영".
/// 코덱 자리에는 합성 코덱을 주입한다(XGT 코덱은 ⛔ spec 게이트).
/// </summary>
public class RackPanelSmokeTests
{
    private static MainWindowViewModel NewViewModel(bool withCodec = true)
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        };

        return new MainWindowViewModel(
            project,
            withCodec ? m => new TestOnlyFrameCodec(new PlcRequestExecutor(m)) : null);
    }

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
    public void WindowOpensWithTheContextRackLaidOut()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.Equal(7, vm.Slots.Count);
        Assert.Equal(2, vm.InputSlots.Count);
        Assert.Single(vm.OutputSlots);
        Assert.Equal(2, vm.AnalogSlots.Count);
        Assert.All(vm.InputSlots, s => Assert.Equal(32, s.Points.Count));
        Assert.Equal(32, vm.OutputSlots[0].Points.Count);
        Assert.All(vm.AnalogSlots, s => Assert.Equal(16, s.Channels.Count));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SlotSubtitlesShowTheMappedAddressRange()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Assert.Contains("%IX512", vm.InputSlots[0].Subtitle, StringComparison.Ordinal);
        Assert.Contains("%QX1024", vm.OutputSlots[0].Subtitle, StringComparison.Ordinal);
        Assert.Contains("%IW80", vm.AnalogSlots[0].Subtitle, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ValueWrittenByTheTestClientShowsUpOnTheOutputLed()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();

        await vm.ToggleServerCommand.ExecuteAsync(null);
        Assert.True(vm.IsServerRunning);

        var led = vm.OutputSlots[0].Points[7];
        Assert.False(led.IsOn);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        var res = await client.WriteIndividualAsync((led.AddressText, [0x01]));
        Assert.True(res.IsSuccess);

        // UI 주기 갱신이 하는 일을 직접 호출한다.
        await WaitUntilAsync(() =>
        {
            vm.Refresh();
            return led.IsOn;
        });

        Assert.True(led.IsOn);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task InputToggleFlippedInTheUiIsSeenByAReadingClient()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        await vm.ToggleServerCommand.ExecuteAsync(null);
        var point = vm.InputSlots[0].Points[5];

        point.IsOn = true;

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        var res = await client.ReadIndividualAsync(point.AddressText);

        Assert.True(res.IsSuccess);
        Assert.Equal(new byte[] { 0x01 }, res.Blocks[0]);

        point.IsOn = false;
        var off = await client.ReadIndividualAsync(point.AddressText);
        Assert.Equal(new byte[] { 0x00 }, off.Blocks[0]);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task AnalogEngineeringInputIsReadableByTheClientAsRaw()
    {
        var project = NxpProject.CreateDefault(port: 0) with
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
        };
        var vm = new MainWindowViewModel(
            project, m => new TestOnlyFrameCodec(new PlcRequestExecutor(m)));
        new MainWindow { DataContext = vm }.Show();

        await vm.ToggleServerCommand.ExecuteAsync(null);

        var channel = vm.AnalogSlots[0].Channels[0];
        Assert.Equal("V", channel.UnitText);

        channel.EngineeringText = "5";

        Assert.Null(channel.Error);
        Assert.Equal("2000", channel.RawText);

        await using var client = await PlcTestClient.ConnectAsync(
            "127.0.0.1", vm.Engine.LocalEndPoint!.Port);
        var res = await client.ReadIndividualAsync(channel.AddressText);

        Assert.Equal(2000, res.FirstWord);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void InvalidAnalogInputShowsAnErrorAndDoesNotWriteMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        var channel = vm.AnalogSlots[0].Channels[0];

        channel.RawText = "not a number";

        Assert.NotNull(channel.Error);
        Assert.Equal(0u, vm.Engine.Memory.ReadScalar(channel.Address));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void PeriodicRefreshDoesNotClobberInProgressTyping()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        var channel = vm.AnalogSlots[0].Channels[0];
        channel.RawText = "123";

        // "12." 처럼 아직 유효하지 않은 입력 중에 주기 갱신이 돌아도 입력칸을 되돌리지 않아야 한다.
        channel.RawText = "12.";
        Assert.NotNull(channel.Error);

        vm.Refresh();

        Assert.Equal("12.", channel.RawText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OutputLedPointsAreNotUserWritable()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Assert.All(vm.OutputSlots[0].Points, p => Assert.False(p.IsWritable));
        Assert.All(vm.InputSlots[0].Points, p => Assert.True(p.IsWritable));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task WithoutACodecTheUiShowsTheSpecGateNoticeAndTheStartButtonIsDisabled()
    {
        var vm = NewViewModel(withCodec: false);
        new MainWindow { DataContext = vm }.Show();

        Assert.False(vm.CanStartServer);
        Assert.True(vm.ShowServerUnavailableNotice);
        Assert.Contains("spec/xgt-fenet-reference.md", vm.ServerUnavailableReason!, StringComparison.Ordinal);
        Assert.Equal("사용 불가", vm.ServerStatusText);

        // 그래도 랙 패널은 전부 조작 가능해야 한다 (게이트는 서버에만 걸린다).
        vm.InputSlots[0].Points[0].IsOn = true;
        Assert.True(vm.Engine.Memory.ReadBit(vm.InputSlots[0].Points[0].Address));

        await vm.ToggleServerCommand.ExecuteAsync(null);
        Assert.False(vm.IsServerRunning);
        Assert.Contains("spec", vm.ErrorMessage!, StringComparison.OrdinalIgnoreCase);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ServerStatusPillReflectsRunningState()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Assert.Equal("정지", vm.ServerStatusText);
        Assert.Equal("시작", vm.StartStopLabel);

        await vm.ToggleServerCommand.ExecuteAsync(null);

        Assert.Contains("수신 중", vm.ServerStatusText, StringComparison.Ordinal);
        Assert.Equal("정지", vm.StartStopLabel);

        await vm.ToggleServerCommand.ExecuteAsync(null);
        Assert.Equal("정지", vm.ServerStatusText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ProjectSaveAndOpenRoundTripsToggleState()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-ui-").FullName;
        try
        {
            var path = Path.Combine(dir, "state.nxp");
            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();

            vm.InputSlots[0].Points[3].IsOn = true;
            vm.InputSlots[1].Points[9].IsOn = true;
            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel();
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            Assert.True(reopened.InputSlots[0].Points[3].IsOn);
            Assert.True(reopened.InputSlots[1].Points[9].IsOn);
            Assert.False(reopened.InputSlots[0].Points[4].IsOn);
            reopened.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void OpeningABrokenProjectShowsAnErrorAndKeepsTheAppUsable()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-ui-bad-").FullName;
        try
        {
            var path = Path.Combine(dir, "broken.nxp");
            File.WriteAllText(path, "{ not json ");

            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();
            vm.OpenProject(path);

            Assert.NotNull(vm.ErrorMessage);
            // 앱은 계속 동작한다.
            vm.InputSlots[0].Points[0].IsOn = true;
            Assert.True(vm.InputSlots[0].Points[0].IsOn);
            vm.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
