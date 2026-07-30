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
/// 낮은 번지 주소 · 사용자 입력 · 출력 비트 조작 회귀.
/// </summary>
/// <remarks>
/// 사용자가 "%MW0 같은 낮은 주소가 안 되고 출력 비트가 안 바뀐다"고 보고해 만든 테스트다.
/// 파서·뷰모델은 정상이었고 실제 원인은 (1) 한글 IME 전각 문자 (2) 출력을 감시 전용으로 둔 설계였다.
/// </remarks>

public class LowAddressAndEditingTests
{
    private static MainWindowViewModel New()
        => new(
            NxpProject.CreateDefault(port: 0) with
            { Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 } },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    [AvaloniaTheory]
    [InlineData("%MW0")]
    [InlineData("%MW000")]
    [InlineData("%MW1")]
    [InlineData("%MX0")]
    [InlineData("%MD0")]
    public void LowAddressCanBeAddedToWatch(string address)
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = address;
        vm.AddWatchCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.Watches);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WatchValueOnLowAddressCanBeChanged()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW0";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.ValueText = "1234";

        Assert.Null(row.Error);
        Assert.Equal(1234u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW0")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WatchValueSurvivesAPeriodicRefreshWhileEditing()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW0";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.ValueText = "1234";
        vm.Refresh();          // 200ms 타이머가 하는 일
        row.ValueText = "5678";
        vm.Refresh();

        Assert.Equal(5678u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW0")));
        Assert.Equal("5678", row.ValueText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OutputGroupBitCanBeToggledByTheUser()
    {
        // 시뮬레이터에서는 사람이 PLC 프로그램 역할을 하므로 %Q 도 직접 켤 수 있어야 한다.
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewOutputPointAddress = "%QW10";
        vm.AddOutputPointCommand.Execute(null);
        var group = vm.OutputGroups[0];

        Assert.True(group.IsWritable);
        Assert.False(group.IsInputMode);

        group.Bits[3].IsOn = true;
        Assert.True(vm.Engine.Memory.ReadBit(group.Bits[3].Address));

        group.Bits[3].IsOn = false;
        Assert.False(vm.Engine.Memory.ReadBit(group.Bits[3].Address));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OutputGroupSetAllWorks()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewOutputPointAddress = "%QW10";
        vm.AddOutputPointCommand.Execute(null);

        vm.OutputGroups[0].SetAllCommand.Execute(null);

        Assert.Equal(0xFFFFu, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%QW10")));
        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("％ＭＷ０")]
    [InlineData("％MW0")]
    [InlineData("%MW０")]
    [InlineData("mw0")]
    [InlineData("MW0")]
    [InlineData(" %MW0 ")]
    public void FullWidthOrSloppyInputIsAcceptedAfterNormalising(string typed)
    {
        // 한글 IME 전각 문자가 실제 실패 원인이었다 — 화면에서 거의 같아 보여 원인을 알기 어렵다.
        var vm = New();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = typed;
        vm.AddWatchCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("%MW0", Assert.Single(vm.Watches).AddressText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void UnparsableInputReportsTheExactCharactersReceived()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = "%ZW1";
        vm.AddWatchCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("U+", vm.ErrorMessage!, StringComparison.Ordinal);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void PeriodicRefreshDoesNotStealTheCaretRightAfterTyping()
    {
        // 마스터가 같은 주소를 폴링하며 쓰는 상황에서도 사용자 입력이 유지되어야 한다.
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW0";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.ValueText = "1234";

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 9999);
        vm.Refresh();

        Assert.Equal("1234", row.ValueText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void InputGroupOnLowAddressWorks()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewInputPointAddress = "%MW0";
        vm.AddInputPointCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        var group = Assert.Single(vm.InputGroups);
        group.Bits[0].IsOn = true;
        group.Bits[15].IsOn = true;

        Assert.Equal(0b1000_0000_0000_0001u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW0")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AnalogOnLowAddressWorks()
    {
        var vm = New();
        new MainWindow { DataContext = vm }.Show();
        vm.NewAnalogAddress = "%MW0";
        vm.NewAnalogRawMin = "0"; vm.NewAnalogRawMax = "4000";
        vm.NewAnalogEuMin = "0"; vm.NewAnalogEuMax = "10"; vm.NewAnalogUnit = "V";
        vm.AddAnalogPointCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        vm.AnalogPoints[0].EngineeringText = "5";
        Assert.Equal(2000u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW0")));
        vm.Shutdown();
    }
}
