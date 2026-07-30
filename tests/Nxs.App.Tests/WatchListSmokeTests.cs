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
/// 사용자 지정 주소 워치 UI — LabVIEW 가 교신하는 임의 주소(%MW320, %MD422 …)를
/// 눈으로 보고 직접 쓸 수 있어야 한다.
/// </summary>
public class WatchListSmokeTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    [AvaloniaFact]
    public void AddingAnAddressCreatesAWatchRow()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = "%MW320";
        vm.NewWatchLabel = "설정 압력";
        vm.AddWatchCommand.Execute(null);

        var row = Assert.Single(vm.Watches);
        Assert.Equal("%MW320", row.AddressText);
        Assert.Equal("설정 압력", row.Label);
        Assert.Equal("WORD", row.SizeText);
        Assert.True(vm.HasWatches);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(vm.NewWatchAddress);

        vm.Shutdown();
    }

    [AvaloniaTheory]
    [InlineData("%MW320", "WORD")]
    [InlineData("%MD422", "DWORD")]
    [InlineData("%MX801", "BIT")]
    [InlineData("%MB40", "BYTE")]
    [InlineData("%QX1024", "BIT")]
    public void EveryAddressFormCanBeWatched(string address, string expectedSize)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = address;
        vm.AddWatchCommand.Execute(null);

        Assert.Equal(expectedSize, Assert.Single(vm.Watches).SizeText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void InvalidAddressIsRejectedWithAHelpfulMessage()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = "%ZW1";
        vm.AddWatchCommand.Execute(null);

        Assert.Empty(vm.Watches);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("%MW320", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void DuplicateAddressIsRejected()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);

        Assert.Single(vm.Watches);
        Assert.Contains("이미 목록에", vm.ErrorMessage!, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WritingIntoTheWatchRowUpdatesMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.ValueText = "4660";

        Assert.Null(row.Error);
        Assert.Equal(4660u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void HexInputIsAccepted()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MD422";
        vm.AddWatchCommand.Execute(null);

        vm.Watches[0].ValueText = "0xDEADBEEF";

        Assert.Null(vm.Watches[0].Error);
        Assert.Equal(0xDEADBEEFu, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MD422")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void GarbageInputShowsAnErrorAndLeavesMemoryAlone()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);

        vm.Watches[0].ValueText = "헛소리";

        Assert.NotNull(vm.Watches[0].Error);
        Assert.Equal(0u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChangingFormatRerendersTheSameValue()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];
        row.ValueText = "4660";

        row.Format = WatchFormat.Hex;
        Assert.Equal("0x1234", row.ValueText);

        row.Format = WatchFormat.Binary;
        Assert.Equal("0001 0010 0011 0100", row.ValueText);

        row.Format = WatchFormat.Decimal;
        Assert.Equal("4660", row.ValueText);

        // 형식만 바꿨을 뿐이므로 메모리는 그대로여야 한다.
        Assert.Equal(4660u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ExternalChangeShowsUpOnRefresh()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);

        // 마스터가 쓴 것을 모사
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW320"), 777);
        vm.Refresh();

        Assert.Equal("777", vm.Watches[0].ValueText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RefreshDoesNotClobberInProgressTyping()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.ValueText = "0x12";   // 아직 입력 중인 부분 값
        vm.Refresh();

        Assert.Equal("0x12", row.ValueText);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RemovingAWatchDropsIt()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);
        var row = vm.Watches[0];

        row.RemoveCommand.Execute(null);

        Assert.Empty(vm.Watches);
        Assert.False(vm.HasWatches);
        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WatchesSurviveProjectSaveAndReopen()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-watch-ui-").FullName;
        try
        {
            var path = Path.Combine(dir, "watch.nxp");
            var vm = NewViewModel();
            new MainWindow { DataContext = vm }.Show();

            vm.NewWatchAddress = "%MW320";
            vm.NewWatchLabel = "설정 압력";
            vm.AddWatchCommand.Execute(null);
            vm.NewWatchAddress = "%MD422";
            vm.NewWatchLabel = "적산 유량";
            vm.AddWatchCommand.Execute(null);
            vm.Watches[1].Format = WatchFormat.Hex;

            vm.SaveProject(path);
            Assert.Null(vm.ErrorMessage);
            vm.Shutdown();

            var reopened = NewViewModel();
            new MainWindow { DataContext = reopened }.Show();
            reopened.OpenProject(path);

            Assert.Null(reopened.ErrorMessage);
            Assert.Equal(2, reopened.Watches.Count);
            Assert.Equal("%MW320", reopened.Watches[0].AddressText);
            Assert.Equal("설정 압력", reopened.Watches[0].Label);
            Assert.Equal("%MD422", reopened.Watches[1].AddressText);
            Assert.Equal(WatchFormat.Hex, reopened.Watches[1].Format);
            reopened.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void CodecIsWiredSoTheServerCanActuallyStart()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Assert.True(vm.CanStartServer);
        Assert.False(vm.ShowServerUnavailableNotice);
        // 초안 상태이므로 미검증 경고가 대신 떠야 한다.
        Assert.True(vm.IsCodecDraft);
        Assert.Contains("spec/xgt-fenet-reference.md", vm.CodecDraftWarning, StringComparison.Ordinal);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public async Task ServerStartsAndAWatchedAddressIsReadableByAnXgtMaster()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        vm.NewWatchAddress = "%MW320";
        vm.AddWatchCommand.Execute(null);

        await vm.ToggleServerCommand.ExecuteAsync(null);
        Assert.True(vm.IsServerRunning);

        vm.Watches[0].ValueText = "1234";

        Assert.Equal(1234u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));
        Assert.Contains("수신 중", vm.ServerStatusText, StringComparison.Ordinal);

        vm.Shutdown();
    }
}
