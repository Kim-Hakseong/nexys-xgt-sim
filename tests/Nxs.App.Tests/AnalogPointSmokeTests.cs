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
/// 사용자 지정 A/D 채널 — 임의 주소 + 스케일로 공학단위 ↔ raw 를 왕복한다.
/// </summary>
public class AnalogPointSmokeTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    private static void Add(MainWindowViewModel vm, string address, string label = "",
        string rawMax = "4000", string euMax = "10", string unit = "V")
    {
        vm.NewAnalogAddress = address;
        vm.NewAnalogLabel = label;
        vm.NewAnalogRawMin = "0";
        vm.NewAnalogRawMax = rawMax;
        vm.NewAnalogEuMin = "0";
        vm.NewAnalogEuMax = euMax;
        vm.NewAnalogUnit = unit;
        vm.AddAnalogPointCommand.Execute(null);
    }

    [AvaloniaFact]
    public void AddingAChannelCreatesARowWithItsScale()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Add(vm, "%IW80", "탱크 압력", euMax: "10", unit: "bar");

        var row = Assert.Single(vm.AnalogPoints);
        Assert.Equal("%IW80", row.AddressText);
        Assert.Equal("탱크 압력", row.Label);
        Assert.Equal("WORD", row.SizeText);
        Assert.Equal("bar", row.UnitText);
        Assert.True(vm.HasAnalogPoints);
        Assert.Null(vm.ErrorMessage);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void EngineeringInputConvertsToRawAndWritesMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%IW80", euMax: "10");
        var row = vm.AnalogPoints[0];

        row.EngineeringText = "5";

        Assert.Null(row.Error);
        Assert.Equal("2000", row.RawText);
        Assert.Equal(2000u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%IW80")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RawInputConvertsToEngineering()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MW600", euMax: "400", unit: "C");
        var row = vm.AnalogPoints[0];

        row.RawText = "1850";

        Assert.Null(row.Error);
        Assert.Equal("185", row.EngineeringText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FractionalEngineeringValueIsSupported()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%IW80", euMax: "10");

        vm.AnalogPoints[0].EngineeringText = "6.25";

        Assert.Equal("2500", vm.AnalogPoints[0].RawText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void MasterWriteShowsUpAsEngineeringValueOnRefresh()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%IW80", euMax: "10");

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%IW80"), 3000);
        vm.Refresh();

        Assert.Equal("7.5", vm.AnalogPoints[0].EngineeringText);
        Assert.Equal("3000", vm.AnalogPoints[0].RawText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void BitAddressIsRejectedForAnalog()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Add(vm, "%MX801");

        Assert.Empty(vm.AnalogPoints);
        Assert.Contains("비트 주소", vm.ErrorMessage!, StringComparison.Ordinal);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DegenerateScaleIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Add(vm, "%IW80", euMax: "0");   // 공학단위 범위 폭 0

        Assert.Empty(vm.AnalogPoints);
        Assert.Contains("공학단위 범위 폭", vm.ErrorMessage!, StringComparison.Ordinal);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void NonNumericScaleIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewAnalogAddress = "%IW80";
        vm.NewAnalogRawMax = "헛소리";
        vm.AddAnalogPointCommand.Execute(null);

        Assert.Empty(vm.AnalogPoints);
        Assert.Contains("raw 범위", vm.ErrorMessage!, StringComparison.Ordinal);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void GarbageValueInputShowsErrorAndLeavesMemoryAlone()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%IW80");

        vm.AnalogPoints[0].EngineeringText = "헛소리";

        Assert.NotNull(vm.AnalogPoints[0].Error);
        Assert.Equal(0u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%IW80")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DuplicateAddressIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Add(vm, "%IW80");
        Add(vm, "%IW80");

        Assert.Single(vm.AnalogPoints);
        Assert.Contains("이미 목록에", vm.ErrorMessage!, StringComparison.Ordinal);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RemovingAChannelDropsIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%IW80");

        vm.AnalogPoints[0].RemoveCommand.Execute(null);

        Assert.Empty(vm.AnalogPoints);
        Assert.False(vm.HasAnalogPoints);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChannelsSurviveProjectSaveAndReopenWithTheirScale()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-ap-").FullName;
        try
        {
            var path = Path.Combine(dir, "analog.nxp");
            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();
            Add(vm, "%IW80", "탱크 압력", euMax: "10", unit: "bar");
            Add(vm, "%MW600", "노즐 온도", euMax: "400", unit: "C");
            vm.AnalogPoints[0].EngineeringText = "5";

            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel();
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            Assert.Equal(2, reopened.AnalogPoints.Count);
            Assert.Equal("탱크 압력", reopened.AnalogPoints[0].Label);
            Assert.Equal("bar", reopened.AnalogPoints[0].UnitText);
            Assert.Equal(400, reopened.AnalogPoints[1].Scale.EngineeringMax);
            // 값도 초기값으로 저장되어 복원된다.
            Assert.Equal("5", reopened.AnalogPoints[0].EngineeringText);
            reopened.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MasterReadsTheValueWrittenThroughTheAnalogChannel()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MW600", euMax: "400", unit: "C");
        await vm.ToggleServerCommand.ExecuteAsync(null);

        vm.AnalogPoints[0].EngineeringText = "200";

        // 200/400 * 4000 = 2000
        Assert.Equal(2000u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW600")));
        vm.Shutdown();
    }
}
