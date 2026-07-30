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
/// 사용자 지정 디지털 점 — 임의 비트 주소를 입력 탭에서 토글하고 출력 탭에서 감시한다.
/// 불리언 ON/OFF 를 양방향으로 확인할 수 있어야 한다.
/// </summary>
public class CustomDigitalPointSmokeTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    [AvaloniaFact]
    public void AddingAnInputPointCreatesAToggleableRow()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewInputPointAddress = "%MX801";
        vm.NewInputPointLabel = "운전 지령";
        vm.AddInputPointCommand.Execute(null);

        var point = Assert.Single(vm.CustomInputPoints);
        Assert.Equal("%MX801", point.AddressText);
        Assert.Equal("운전 지령", point.Label);
        Assert.True(point.IsWritable);
        Assert.Equal("OFF", point.StateText);
        Assert.True(vm.HasCustomInputPoints);
        Assert.Null(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AddingAnOutputPointCreatesAMonitorOnlyRow()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewOutputPointAddress = "%QX2000";
        vm.AddOutputPointCommand.Execute(null);

        var point = Assert.Single(vm.CustomOutputPoints);
        Assert.False(point.IsWritable);
        Assert.True(vm.HasCustomOutputPoints);
        Assert.Empty(vm.CustomInputPoints);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TogglingAnInputPointWritesTheBit()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewInputPointAddress = "%MX801";
        vm.AddInputPointCommand.Execute(null);
        var point = vm.CustomInputPoints[0];

        point.IsOn = true;

        Assert.True(vm.Engine.Memory.ReadBit(IecAddress.Parse("%MX801")));
        Assert.Equal("ON", point.StateText);

        point.IsOn = false;
        Assert.False(vm.Engine.Memory.ReadBit(IecAddress.Parse("%MX801")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OutputPointDoesNotWriteWhenItsStateChanges()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewOutputPointAddress = "%QX2000";
        vm.AddOutputPointCommand.Execute(null);

        // 감시 전용이므로 IsOn 을 건드려도 메모리에 쓰지 않는다.
        vm.CustomOutputPoints[0].IsOn = true;

        Assert.False(vm.Engine.Memory.ReadBit(IecAddress.Parse("%QX2000")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ExternalWriteShowsUpOnBothInputAndOutputPoints()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewInputPointAddress = "%MX801";
        vm.AddInputPointCommand.Execute(null);
        vm.NewOutputPointAddress = "%QX2000";
        vm.AddOutputPointCommand.Execute(null);

        // 마스터가 쓴 것을 모사 — 두 방향 모두 표시가 따라가야 한다.
        vm.Engine.Memory.WriteBit(IecAddress.Parse("%MX801"), true);
        vm.Engine.Memory.WriteBit(IecAddress.Parse("%QX2000"), true);
        vm.Refresh();

        Assert.True(vm.CustomInputPoints[0].IsOn);
        Assert.True(vm.CustomOutputPoints[0].IsOn);
        Assert.Equal("ON", vm.CustomOutputPoints[0].StateText);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("%MW320")]
    [InlineData("%MD422")]
    [InlineData("%ML50")]
    [InlineData("%ZX1")]
    [InlineData("")]
    public void NonBitAddressIsRejected(string address)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewInputPointAddress = address;
        vm.AddInputPointCommand.Execute(null);

        Assert.Empty(vm.CustomInputPoints);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("비트 주소", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DuplicateAddressWithinTheSameDirectionIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewInputPointAddress = "%MX801";
        vm.AddInputPointCommand.Execute(null);
        vm.NewInputPointAddress = "%MX801";
        vm.AddInputPointCommand.Execute(null);

        Assert.Single(vm.CustomInputPoints);
        Assert.Contains("이미 목록에", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RemovingAPointDropsIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewInputPointAddress = "%MX801";
        vm.AddInputPointCommand.Execute(null);

        vm.CustomInputPoints[0].RemoveCommand.Execute(null);

        Assert.Empty(vm.CustomInputPoints);
        Assert.False(vm.HasCustomInputPoints);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void PointsSurviveProjectSaveAndReopenWithTheirDirection()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-dp-").FullName;
        try
        {
            var path = Path.Combine(dir, "points.nxp");
            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();

            vm.NewInputPointAddress = "%MX801";
            vm.NewInputPointLabel = "운전 지령";
            vm.AddInputPointCommand.Execute(null);
            vm.NewOutputPointAddress = "%QX2000";
            vm.NewOutputPointLabel = "운전 상태";
            vm.AddOutputPointCommand.Execute(null);
            vm.CustomInputPoints[0].IsOn = true;

            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel();
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            var input = Assert.Single(reopened.CustomInputPoints);
            var output = Assert.Single(reopened.CustomOutputPoints);
            Assert.Equal("%MX801", input.AddressText);
            Assert.Equal("운전 지령", input.Label);
            Assert.True(input.IsWritable);
            Assert.Equal("%QX2000", output.AddressText);
            Assert.False(output.IsWritable);
            // 켜 둔 상태가 초기값으로 저장되어 복원된다.
            Assert.True(input.IsOn);
            reopened.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task InputPointToggleIsReadableByTheMasterAndMasterWriteIsVisible()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewInputPointAddress = "%MX1200";
        vm.AddInputPointCommand.Execute(null);
        await vm.ToggleServerCommand.ExecuteAsync(null);

        var point = vm.CustomInputPoints[0];

        // 방향 1: UI 토글 → 메모리
        point.IsOn = true;
        Assert.True(vm.Engine.Memory.ReadBit(point.Address));

        // 방향 2: 외부(마스터) 쓰기 → UI
        vm.Engine.Memory.WriteBit(point.Address, false);
        vm.Refresh();
        Assert.False(point.IsOn);

        vm.Shutdown();
    }
}
