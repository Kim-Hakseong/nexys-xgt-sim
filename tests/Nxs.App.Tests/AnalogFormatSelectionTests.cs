using System.Buffers.Binary;
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
/// A/D 채널의 raw 표시 형식 · 바이트 순서 선택 — 워치와 같은 선택을 A/D 에도 둔다.
/// </summary>
/// <remarks>
/// 마스터가 워드에 IEEE754 실수를 넣으면 정수로 읽은 raw 가 999999961 같은 값이 되어
/// 스케일 환산이 무의미해진다. 채널마다 형식을 고를 수 있어야 값 기준을 맞출 수 있다.
/// </remarks>
public class AnalogFormatSelectionTests
{
    private static MainWindowViewModel NewViewModel(NxpProject? project = null)
        => new(
            project ?? NxpProject.CreateDefault(port: 0) with
            {
                Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            },
            memory => new XgtFenetCodec(new PlcRequestExecutor(memory)));

    private static void Add(MainWindowViewModel vm, string address, string rawMax = "4000", string euMax = "10")
    {
        vm.NewAnalogAddress = address;
        vm.NewAnalogLabel = string.Empty;
        vm.NewAnalogRawMin = "0";
        vm.NewAnalogRawMax = rawMax;
        vm.NewAnalogEuMin = "0";
        vm.NewAnalogEuMax = euMax;
        vm.NewAnalogUnit = "V";
        vm.AddAnalogPointCommand.Execute(null);
    }

    [AvaloniaFact]
    public void DefaultFormatIsSignedSoExistingBehaviourIsUnchanged()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MW320");

        var row = vm.AnalogPoints[0];
        Assert.Equal(WatchFormat.Signed, row.Format);
        Assert.Equal(ByteOrder.Dcba, row.Order);

        row.EngineeringText = "5";
        Assert.Equal("2000", row.RawText);
        Assert.Equal(2000u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void BoolIsNotOfferedBecauseItIsMeaninglessForAnAnalogChannel()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD220");

        var row = vm.AnalogPoints[0];
        Assert.DoesNotContain(WatchFormat.Bool, row.Formats);
        Assert.Contains(WatchFormat.Signed, row.Formats);
        Assert.Contains(WatchFormat.Float, row.Formats);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void WidthDecidesWhichFormatsAreOffered()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MW320");   // 2바이트
        Add(vm, "%MD400");   // 4바이트
        Add(vm, "%ML60");    // 8바이트

        var word = vm.AnalogPoints[0];
        var dword = vm.AnalogPoints[1];
        var lword = vm.AnalogPoints[2];

        // Float 은 4바이트, Double 은 8바이트 주소에서만 의미가 있다.
        Assert.DoesNotContain(WatchFormat.Float, word.Formats);
        Assert.DoesNotContain(WatchFormat.Double, word.Formats);
        Assert.Contains(WatchFormat.Float, dword.Formats);
        Assert.DoesNotContain(WatchFormat.Double, dword.Formats);
        Assert.Contains(WatchFormat.Double, lword.Formats);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void SwitchingToFloatReadsTheSameBytesAsARealNumber()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD220", rawMax: "4000", euMax: "10");

        // 마스터가 100.0f 를 4바이트에 넣었다고 하자.
        var msb = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(msb, 100.0f);
        vm.Engine.Memory.WriteRaw(
            IecAddress.Parse("%MD220"), WatchValue.FromMsbFirst(msb, ByteOrder.Dcba));

        var row = vm.AnalogPoints[0];
        row.Refresh();

        // 정수로 읽으면 사람이 알아볼 수 없는 값이 나온다 — 그게 사용자가 본 증상이다.
        Assert.Equal("1120403456", row.RawText);

        row.Format = WatchFormat.Float;

        Assert.Equal("100", row.RawText);
        Assert.Equal("0.25", row.EngineeringText);   // raw 100 / 4000 × 10 V

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ChangingFormatDoesNotTouchMemory()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD400");

        var address = IecAddress.Parse("%MD400");
        var row = vm.AnalogPoints[0];
        row.RawText = "1234";
        var before = vm.Engine.Memory.ReadRaw(address);

        row.Format = WatchFormat.Hex;
        Assert.Equal("0x000004D2", row.RawText);
        row.Format = WatchFormat.Float;

        // 형식만 바꿨을 뿐이므로 메모리는 그대로여야 한다.
        Assert.Equal(before, vm.Engine.Memory.ReadRaw(address));

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void EngineeringInputInFloatFormatWritesIeee754Bytes()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD400", rawMax: "4000", euMax: "10");

        var row = vm.AnalogPoints[0];
        row.Format = WatchFormat.Float;
        row.EngineeringText = "5";

        // raw 2000.0 이 실수로 들어가야 한다 — 정수 비트가 아니다.
        var stored = vm.Engine.Memory.ReadRaw(IecAddress.Parse("%MD400"));
        var asFloat = WatchValue.ToNumber(stored, WatchFormat.Float, ByteOrder.Dcba);
        Assert.Equal(2000.0, asFloat!.Value, 3);
        Assert.Equal("2000", row.RawText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FloatFormatKeepsTheFractionThatIntegerFormatWouldLose()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD400", rawMax: "4000", euMax: "10");

        var row = vm.AnalogPoints[0];
        row.Format = WatchFormat.Float;
        row.EngineeringText = "5.001";

        // raw 2000.4 — 정수 형식이라면 2000 으로 깎였다.
        var stored = vm.Engine.Memory.ReadRaw(IecAddress.Parse("%MD400"));
        Assert.Equal(2000.4, WatchValue.ToNumber(stored, WatchFormat.Float, ByteOrder.Dcba)!.Value, 3);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ByteOrderChangeReinterpretsWithoutWriting()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD400");

        var address = IecAddress.Parse("%MD400");
        var row = vm.AnalogPoints[0];
        row.RawText = "1234";
        var before = vm.Engine.Memory.ReadRaw(address);

        row.Order = ByteOrder.Abcd;

        Assert.NotEqual("1234", row.RawText);
        Assert.Equal(before, vm.Engine.Memory.ReadRaw(address));
        Assert.Equal("ABCD (빅엔디안)", row.OrderText);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void ByteOrderIsHiddenForSingleByteAddresses()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MB40");

        var row = vm.AnalogPoints[0];
        Assert.False(row.SupportsByteOrder);
        Assert.Empty(row.OrderOptions);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void RawInputOutsideTheFormatRangeIsReportedNotTruncated()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MW320");

        var row = vm.AnalogPoints[0];
        row.Format = WatchFormat.Signed;

        // 40000 은 WORD 16비트에 담기므로 받아들이되 부호 있는 표기(-25536)로 즉시 되비춘다.
        // 마스터가 보는 비트는 같다 — 조용히 바뀌는 것이 아니라 화면에서 바로 드러난다.
        row.RawText = "40000";
        Assert.Null(row.Error);
        Assert.Equal(40000u, vm.Engine.Memory.ReadScalar(IecAddress.Parse("%MW320")));

        // 16비트에 아예 담기지 않는 값은 거절하고 이유를 알린다.
        row.RawText = "70000";
        Assert.NotNull(row.Error);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void FormatAndOrderSurviveAProjectRoundTrip()
    {
        var vm = NewViewModel();
        new MainWindow { DataContext = vm }.Show();
        Add(vm, "%MD400");

        vm.AnalogPoints[0].Format = WatchFormat.Float;
        vm.AnalogPoints[0].Order = ByteOrder.Abcd;

        var saved = vm.BuildProjectSnapshot();
        var entry = Assert.Single(saved.AnalogPoints);
        Assert.Equal(WatchFormat.Float, entry.Format);
        Assert.Equal(ByteOrder.Abcd, entry.Order);

        vm.Shutdown();

        // 저장한 프로젝트를 다시 열면 선택이 복원되어야 한다.
        var reopened = NewViewModel(saved with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        });
        Assert.Equal(WatchFormat.Float, reopened.AnalogPoints[0].Format);
        Assert.Equal(ByteOrder.Abcd, reopened.AnalogPoints[0].Order);
        Assert.Same(
            reopened.AnalogPoints[0].SelectedFormatOption,
            reopened.AnalogPoints[0].FormatOptions.Single(o => o.Value == WatchFormat.Float));

        reopened.Shutdown();
    }

    [AvaloniaFact]
    public void SavedFormatThatDoesNotFitTheWidthFallsBackInsteadOfCrashing()
    {
        // 손으로 고친 .nxp 나 주소 폭을 바꾼 경우 — 조용히 첫 형식으로 되돌린다.
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
            AnalogPoints =
            [
                new AnalogPointEntry { Address = "%MW320", Format = WatchFormat.Double },
            ],
        };

        var vm = NewViewModel(project);
        var row = vm.AnalogPoints[0];
        Assert.Contains(row.Format, row.Formats);
        Assert.NotEqual(WatchFormat.Double, row.Format);

        vm.Shutdown();
    }

    [AvaloniaFact]
    public void AnalogTabRendersBothCombosForEachChannel()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1240, Height = 860 };
        window.Show();
        Add(vm, "%MD220");

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabControl.SelectedIndex = 1;   // A/D 입력
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();

        var row = vm.AnalogPoints[0];
        var combos = window.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => ReferenceEquals(c.DataContext, row))
            .ToList();

        Assert.Equal(2, combos.Count);
        Assert.Contains(combos, c => ReferenceEquals(c.SelectedItem, row.SelectedFormatOption));
        Assert.Contains(combos, c => ReferenceEquals(c.SelectedItem, row.SelectedOrderOption));

        vm.Shutdown();
    }
}
