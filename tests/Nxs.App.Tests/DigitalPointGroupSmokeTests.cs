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
/// 사용자 지정 디지털 점 그룹 — 임의 주소를 비트 배열로 펼쳐 입력 탭에서 토글하고 출력 탭에서 감시한다.
/// 불리언 ON/OFF 를 양방향으로 확인할 수 있어야 한다.
/// </summary>
public class DigitalPointGroupSmokeTests
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

        vm.NewDigitalAddress = "%MX801";
        vm.NewDigitalLabel = "운전 지령";
        vm.AddDigitalPointCommand.Execute(null);

        var group = Assert.Single(vm.DigitalGroups);
        Assert.Equal("%MX801", group.AddressText);
        Assert.Equal("운전 지령", group.Label);
        Assert.True(group.IsWritable);
        Assert.Equal(1, group.BitCount);
        Assert.False(group.IsArray);
        Assert.Equal("OFF", group.ValueText);
        Assert.True(vm.HasDigitalGroups);
        Assert.Null(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void EveryPointIsBidirectionalRegardlessOfArea()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewDigitalAddress = "%QX2000";
        vm.AddDigitalPointCommand.Execute(null);

        var group = Assert.Single(vm.DigitalGroups);
        // %Q 도 직접 조작할 수 있다 — 시뮬레이터에서는 사람이 PLC 프로그램 역할을 한다.
        Assert.True(group.IsWritable);
        Assert.True(vm.HasDigitalGroups);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TogglingAnInputPointWritesTheBit()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MX801";
        vm.AddDigitalPointCommand.Execute(null);
        var group = vm.DigitalGroups[0];

        group.Bits[0].IsOn = true;

        Assert.True(vm.Engine.Memory.ReadBit(IecAddress.Parse("%MX801")));
        Assert.Equal("ON", group.ValueText);

        group.Bits[0].IsOn = false;
        Assert.False(vm.Engine.Memory.ReadBit(IecAddress.Parse("%MX801")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void OutputPointWritesWhenTheUserTogglesIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%QX2000";
        vm.AddDigitalPointCommand.Execute(null);

        // 마스터가 읽을 %Q 값을 사람이 만들 수 있어야 한다.
        vm.DigitalGroups[0].Bits[0].IsOn = true;

        Assert.True(vm.Engine.Memory.ReadBit(IecAddress.Parse("%QX2000")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ExternalWriteShowsUpOnBothInputAndOutputPoints()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MX801";
        vm.AddDigitalPointCommand.Execute(null);
        vm.NewDigitalAddress = "%QX2000";
        vm.AddDigitalPointCommand.Execute(null);

        // 마스터가 쓴 것을 모사 — 두 방향 모두 표시가 따라가야 한다.
        vm.Engine.Memory.WriteBit(IecAddress.Parse("%MX801"), true);
        vm.Engine.Memory.WriteBit(IecAddress.Parse("%QX2000"), true);
        vm.Refresh();

        Assert.True(vm.DigitalGroups[0].Bits[0].IsOn);
        Assert.True(vm.DigitalGroups[0].Bits[0].IsOn);
        Assert.Equal("ON", vm.DigitalGroups[0].ValueText);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("%MX801", 1)]
    [InlineData("%MB40", 8)]
    [InlineData("%MW320", 16)]
    [InlineData("%MD422", 32)]
    [InlineData("%ML50", 64)]
    public void EveryWidthExpandsIntoThatManyBits(string address, int expectedBits)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewDigitalAddress = address;
        vm.AddDigitalPointCommand.Execute(null);

        var group = Assert.Single(vm.DigitalGroups);
        Assert.Equal(expectedBits, group.BitCount);
        Assert.Equal(expectedBits, group.Bits.Count);
        Assert.Equal(expectedBits > 1, group.IsArray);
        Assert.Null(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("%ZX1")]
    [InlineData("")]
    [InlineData("헛소리")]
    public void InvalidAddressIsRejected(string address)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewDigitalAddress = address;
        vm.AddDigitalPointCommand.Execute(null);

        Assert.Empty(vm.DigitalGroups);
        Assert.NotNull(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WordGroupBitsMapOntoTheWordValue()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MW320";
        vm.AddDigitalPointCommand.Execute(null);
        var group = vm.DigitalGroups[0];

        group.Bits[0].IsOn = true;
        group.Bits[1].IsOn = true;
        group.Bits[3].IsOn = true;

        Assert.Equal(0b1011u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WordWrittenByTheMasterLightsTheMatchingBits()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MW900";
        vm.AddDigitalPointCommand.Execute(null);
        var group = vm.DigitalGroups[0];

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW900"), 0b1000_0001_0000_0101);
        vm.Refresh();

        Assert.True(group.Bits[0].IsOn);
        Assert.False(group.Bits[1].IsOn);
        Assert.True(group.Bits[2].IsOn);
        Assert.True(group.Bits[8].IsOn);
        Assert.True(group.Bits[15].IsOn);
        Assert.Equal("0x8105", group.ValueText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SetAllAndClearAllDriveEveryBitOfAnInputGroup()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MW320";
        vm.AddDigitalPointCommand.Execute(null);
        var group = vm.DigitalGroups[0];

        group.SetAllCommand.Execute(null);
        Assert.Equal(0xFFFFu, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        group.ClearAllCommand.Execute(null);
        Assert.Equal(0u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SetAllWorksOnAnOutputGroupToo()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MW900";
        vm.AddDigitalPointCommand.Execute(null);

        vm.DigitalGroups[0].SetAllCommand.Execute(null);

        Assert.Equal(0xFFFFu, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW900")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DuplicateAddressWithinTheSameDirectionIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewDigitalAddress = "%MX801";
        vm.AddDigitalPointCommand.Execute(null);
        vm.NewDigitalAddress = "%MX801";
        vm.AddDigitalPointCommand.Execute(null);

        Assert.Single(vm.DigitalGroups);
        Assert.Contains("이미 목록에", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RemovingAPointDropsIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewDigitalAddress = "%MX801";
        vm.AddDigitalPointCommand.Execute(null);

        vm.DigitalGroups[0].RemoveCommand.Execute(null);

        Assert.Empty(vm.DigitalGroups);
        Assert.False(vm.HasDigitalGroups);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void PointsSurviveProjectSaveAndReopen()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-dp-").FullName;
        try
        {
            var path = Path.Combine(dir, "points.nxp");
            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();

            vm.NewDigitalAddress = "%MX801";
            vm.NewDigitalLabel = "운전 지령";
            vm.AddDigitalPointCommand.Execute(null);
            vm.NewDigitalAddress = "%QX2000";
            vm.NewDigitalLabel = "운전 상태";
            vm.AddDigitalPointCommand.Execute(null);
            vm.DigitalGroups[0].Bits[0].IsOn = true;

            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel();
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            Assert.Equal(2, reopened.DigitalGroups.Count);
            var input = reopened.DigitalGroups[0];
            var output = reopened.DigitalGroups[1];
            Assert.Equal("%MX801", input.AddressText);
            Assert.Equal("운전 지령", input.Label);
            Assert.Equal("%QX2000", output.AddressText);
            // 켜 둔 상태가 초기값으로 저장되어 복원된다.
            Assert.True(input.Bits[0].IsOn);
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
        vm.NewDigitalAddress = "%MX1200";
        vm.AddDigitalPointCommand.Execute(null);
        await vm.ToggleServerCommand.ExecuteAsync(null);

        var bit = vm.DigitalGroups[0].Bits[0];

        // 방향 1: UI 토글 → 메모리
        bit.IsOn = true;
        Assert.True(vm.Engine.Memory.ReadBit(bit.Address));

        // 방향 2: 외부(마스터) 쓰기 → UI
        vm.Engine.Memory.WriteBit(bit.Address, false);
        vm.Refresh();
        Assert.False(bit.IsOn);

        vm.Shutdown();
    }
}
