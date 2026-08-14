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
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// 묶음 탭 — 묶인 주소들은 항상 같은 값을 갖는다.
/// </summary>
/// <remarks>
/// 마스터가 쓰든 사용자가 화면에서 쓰든 전파가 일어나야 한다. 전파는 PlcMemory 에 있으므로
/// 여기서는 **화면에서 조작했을 때** 그 결과가 다른 탭에도 보이는지를 확인한다.
/// </remarks>
public class LinkGroupTabTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    private static void Link(MainWindowViewModel vm, string addresses, string label = "")
    {
        vm.NewLinkAddresses = addresses;
        vm.NewLinkLabel = label;
        vm.AddLinkGroupCommand.Execute(null);
    }

    private static uint Read(MainWindowViewModel vm, string address)
        => vm.Engine.Memory.ReadScalar(IecAddress.Parse(address));

    [AvaloniaFact]
    public void LinkingTwoWordsMakesOneFollowTheOther()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Link(vm, "%MW0 %MW1");

        Assert.Single(vm.LinkGroups);
        Assert.True(vm.HasLinkGroups);

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 1);
        Assert.Equal(1u, Read(vm, "%MW1"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void BitOfWordNotationWorksAcrossWords()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        // 사용자가 말한 그대로: MW0 의 10번 비트 ↔ MW1 의 10번 비트.
        Link(vm, "%MW0.10 %MW1.10");

        Assert.Equal(["%MX10", "%MX26"], vm.LinkGroups[0].AddressTexts);

        vm.Engine.Memory.WriteBit(IecAddress.Parse("%MX10"), true);
        Assert.Equal(1u << 10, Read(vm, "%MW1"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TwoBitsInsideOneWordCanBeLinked()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Link(vm, "%MW0.10 %MW0.12");

        vm.Engine.Memory.WriteBit(IecAddress.Parse("%MX10"), true);
        Assert.Equal((1u << 10) | (1u << 12), Read(vm, "%MW0"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TogglingADigitalBitOnScreenPropagatesToItsPartner()
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            DigitalPoints = [new DigitalPointEntry { Address = "%MW0" }, new DigitalPointEntry { Address = "%MW1" }],
        };

        var vm = NewViewModel(project);
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0.10 %MW1.10");

        // 화면에서 비트를 눌렀을 때도 전파되어야 한다 — 마스터 경로만 되면 반쪽이다.
        vm.DigitalGroups[0].Bits[10].IsOn = true;
        vm.Refresh();

        Assert.True(vm.DigitalGroups[1].Bits[10].IsOn);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AnAnalogChannelDrivesItsLinkedPartner()
    {
        var scale = new AnalogChannelScale
        {
            RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
        };

        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            AnalogPoints =
            [
                new AnalogPointEntry { Address = "%MW100", Scale = scale },
                new AnalogPointEntry { Address = "%MW200", Scale = scale },
            ],
        };

        var vm = NewViewModel(project);
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW100 %MW200");

        vm.AnalogPoints[0].EngineeringText = "5";
        vm.Refresh();

        Assert.Equal(2000u, Read(vm, "%MW200"));
        Assert.Equal("2000", vm.AnalogPoints[1].RawText);
        Assert.Equal("5", vm.AnalogPoints[1].EngineeringText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AMasterWriteAlsoPropagates()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0 %MW1");

        // 코덱을 거친 쓰기 — 실행기 → PlcMemory 경로.
        var executor = new PlcRequestExecutor(vm.Engine.Memory);
        executor.Execute(new WriteIndividualRequest(
            [new PlcWriteItem(IecAddress.Parse("%MW0"), [0x34, 0x12])]));

        Assert.Equal(0x1234u, Read(vm, "%MW1"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ThreeAddressesCanShareAValue()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Link(vm, "%MW0, %MW5, %MW9");

        Assert.Equal(3, vm.LinkGroups[0].AddressTexts.Count);
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW9"), 42);
        Assert.Equal(42u, Read(vm, "%MW0"));
        Assert.Equal(42u, Read(vm, "%MW5"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RemovingAGroupStopsThePropagation()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0 %MW1");

        vm.LinkGroups[0].RemoveCommand.Execute(null);

        Assert.Empty(vm.LinkGroups);
        Assert.False(vm.HasLinkGroups);

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 7);
        Assert.Equal(0u, Read(vm, "%MW1"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ClearingRemovesEveryGroup()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0 %MW1");
        Link(vm, "%MW2 %MW3");

        vm.ClearLinkGroupsCommand.Execute(null);

        Assert.Empty(vm.LinkGroups);
        Assert.True(vm.Engine.Memory.Links.IsEmpty);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheSharedValueIsShownOnTheRow()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0 %MW1");

        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 0x1234);
        vm.Refresh();

        Assert.Equal("0x1234", vm.LinkGroups[0].ValueText);
        Assert.Equal("WORD", vm.LinkGroups[0].SizeText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ABitGroupShowsOnOff()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0.10 %MW0.12");

        Assert.Equal("OFF", vm.LinkGroups[0].ValueText);
        Assert.Equal("BIT", vm.LinkGroups[0].SizeText);

        vm.Engine.Memory.WriteBit(IecAddress.Parse("%MX10"), true);
        vm.Refresh();
        Assert.Equal("ON", vm.LinkGroups[0].ValueText);

        vm.Shutdown();
    }

    // ==================== 잘못된 입력 ====================

    [AvaloniaTheory]
    [InlineData("%MW0")]                 // 하나뿐
    [InlineData("")]
    [InlineData("%MW0 %MD0")]            // 크기가 다름
    [InlineData("%MW0 %MW0")]            // 같은 주소 두 번
    [InlineData("%MW0 없는주소")]
    [InlineData("%MW0.99 %MW1.99")]      // 워드에 없는 비트
    public void BadInputExplainsItselfAndAddsNothing(string input)
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Link(vm, input);

        Assert.Empty(vm.LinkGroups);
        Assert.NotEmpty(vm.LinkNotice);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FullWidthTypedAddressesAreNormalisedLikeEverywhereElse()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();

        Link(vm, "％ｍｗ０ ％ｍｗ１");

        Assert.Equal(["%MW0", "%MW1"], vm.LinkGroups[0].AddressTexts);

        vm.Shutdown();
    }

    // ==================== 저장·복원 ====================

    [AvaloniaFact]
    public void GroupsSurviveAProjectRoundTrip()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Link(vm, "%MW0 %MW1", "운전 지령 미러");
        Link(vm, "%MW0.10 %MW0.12");

        var saved = vm.BuildProjectSnapshot();
        Assert.Equal(2, saved.Links.Count);
        Assert.Equal("운전 지령 미러", saved.Links[0].Label);
        vm.Shutdown();

        var reopened = NewViewModel(saved with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        });

        Assert.Equal(2, reopened.LinkGroups.Count);
        Assert.Equal(["%MW0", "%MW1"], reopened.LinkGroups[0].AddressTexts);
        Assert.Equal("운전 지령 미러", reopened.LinkGroups[0].Label);

        // 복원된 묶음도 실제로 동작해야 한다.
        reopened.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 8);
        Assert.Equal(8u, Read(reopened, "%MW1"));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void InitialValuesAreAppliedBeforeLinksSoTheyAreNotOverwritten()
    {
        // 묶음을 먼저 걸면 초기값 하나가 묶음 전체를 덮어쓴다 — 순서가 중요하다.
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            InitialValues =
            [
                new InitialValue { Address = "%MW0", Value = 11 },
                new InitialValue { Address = "%MW1", Value = 22 },
            ],
            Links = [new MemoryLinkGroup { Addresses = ["%MW0", "%MW1"] }],
        };

        var vm = NewViewModel(project);

        // 초기값이 그대로 들어가 있어야 한다(전파는 다음 쓰기부터).
        Assert.Equal(11u, Read(vm, "%MW0"));
        Assert.Equal(22u, Read(vm, "%MW1"));

        // 묶음은 걸려 있다.
        vm.Engine.Memory.WriteScalar(IecAddress.Parse("%MW0"), 33);
        Assert.Equal(33u, Read(vm, "%MW1"));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ABrokenGroupInTheProjectIsSkippedAndReportedNotFatal()
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            Links =
            [
                new MemoryLinkGroup { Addresses = ["%MW0", "%MD0"] },   // 크기가 다름
                new MemoryLinkGroup { Addresses = ["%MW4", "%MW5"] },
            ],
        };

        var vm = NewViewModel(project);

        // 나머지는 살아야 한다 — 묶음 하나 때문에 프로젝트가 안 열리면 곤란하다.
        Assert.Single(vm.LinkGroups);
        Assert.Equal(["%MW4", "%MW5"], vm.LinkGroups[0].AddressTexts);
        Assert.NotEmpty(vm.LinkNotice);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void TheLinksTabRendersItsRows()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        Link(vm, "%MW0 %MW1", "운전 지령 미러");

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 3;   // 묶음
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("%MW0", texts);
        Assert.Contains("%MW1", texts);
        Assert.Contains("운전 지령 미러", texts);

        vm.Shutdown();
    }
}
