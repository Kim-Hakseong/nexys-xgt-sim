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
/// 프로젝트 저장·복원 — **주소만이 아니라 값도** 돌아와야 한다.
/// </summary>
/// <remarks>
/// 실제 버그: 저장하면 주소 목록(%MW100 등)은 남는데 넣어 둔 값이 사라졌다.
/// 저장 시 워치 값이 초기값 목록에 아예 들어가지 않았기 때문이다.
/// </remarks>
public class ProjectValuePersistenceTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    private static MainWindowViewModel Reopen(MainWindowViewModel vm)
    {
        var saved = vm.BuildProjectSnapshot();
        vm.Shutdown();
        return NewViewModel(saved with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        });
    }

    [AvaloniaFact]
    public void WatchValueSurvivesSaveAndReopen()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW100";
        vm.AddWatchCommand.Execute(null);
        vm.Watches[0].ValueText = "1234";

        var reopened = Reopen(vm);

        // 주소만 남고 값이 사라지던 그 버그다.
        Assert.Equal("%MW100", reopened.Watches[0].AddressText);
        Assert.Equal("1234", reopened.Watches[0].ValueText);
        Assert.Equal(1234u, reopened.Engine.Memory.ReadScalar(IecAddress.Parse("%MW100")));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void DigitalWordValueSurvivesIncludingOffBits()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MW320";
        vm.AddDigitalPointCommand.Execute(null);
        vm.DigitalGroups[0].Bits[1].IsOn = true;
        vm.DigitalGroups[0].Bits[10].IsOn = true;

        var reopened = Reopen(vm);

        Assert.True(reopened.DigitalGroups[0].Bits[1].IsOn);
        Assert.True(reopened.DigitalGroups[0].Bits[10].IsOn);
        Assert.False(reopened.DigitalGroups[0].Bits[0].IsOn);
        Assert.Equal((1u << 1) | (1u << 10),
            reopened.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void AnalogEngineeringValueSurvives()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewAnalogAddress = "%IW80";
        vm.NewAnalogRawMin = "0";
        vm.NewAnalogRawMax = "4000";
        vm.NewAnalogEuMin = "0";
        vm.NewAnalogEuMax = "10";
        vm.NewAnalogUnit = "V";
        vm.AddAnalogPointCommand.Execute(null);
        vm.AnalogPoints[0].EngineeringText = "5";

        var reopened = Reopen(vm);

        Assert.Equal("2000", reopened.AnalogPoints[0].RawText);
        Assert.Equal("5", reopened.AnalogPoints[0].EngineeringText);

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void LwordWatchValueSurvivesViaTheDwordSplit()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%ML50";
        vm.AddWatchCommand.Execute(null);
        vm.Engine.Memory.WriteRaw(IecAddress.Parse("%ML50"),
            [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]);

        var reopened = Reopen(vm);

        // %ML 은 32비트 초기값에 담기지 않아 더블워드 두 개로 쪼개 저장한다.
        Assert.Equal(
            new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 },
            reopened.Engine.Memory.ReadRaw(IecAddress.Parse("%ML50")));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void LinkedAddressValuesSurviveEvenWithoutAWatch()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewLinkAddresses = "%MW0 %MW1";
        vm.AddLinkGroupCommand.Execute(null);
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 77);

        var reopened = Reopen(vm);

        Assert.Equal(77u, reopened.Engine.Memory.ReadScalar(IecAddress.Parse("%MW0")));
        Assert.Equal(77u, reopened.Engine.Memory.ReadScalar(IecAddress.Parse("%MW1")));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void ZeroValuesAddNothingToTheProjectFile()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW100";
        vm.AddWatchCommand.Execute(null);

        var saved = vm.BuildProjectSnapshot();

        // 메모리는 0에서 시작한다 — 0을 저장하면 파일만 커진다.
        Assert.Empty(saved.InitialValues);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheSameAddressInWatchAndDigitalIsSavedOnce()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        vm.NewDigitalAddress = "%MW320";
        vm.AddDigitalPointCommand.Execute(null);
        vm.Watches[0].ValueText = "7";

        var saved = vm.BuildProjectSnapshot();

        Assert.Single(saved.InitialValues);
        Assert.Equal(7u, saved.InitialValues[0].Value);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void HighAddressValuesSurviveToo()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW700";
        vm.AddWatchCommand.Execute(null);
        vm.Watches[0].ValueText = "4660";

        var reopened = Reopen(vm);

        Assert.Equal(4660u, reopened.Engine.Memory.ReadScalar(IecAddress.Parse("%MW700")));

        reopened.Shutdown();
    }
}
