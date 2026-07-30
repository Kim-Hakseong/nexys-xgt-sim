using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.DocShots;

/// <summary>
/// README 용 스크린샷 생성기 (개발 도구).
/// </summary>
/// <remarks>
/// <para>
/// 헤드리스 Skia 로 실제 <see cref="MainWindow"/> 를 렌더해 PNG 로 저장한다.
/// 창을 화면에서 캡처하는 방식과 달리 재현 가능하며 CI 에서도 돌 수 있다.
/// </para>
/// <para>
/// **코덱을 주입하지 않는다** — 운영 배포와 동일한 상태(⛔ FEnet 게이트)로 렌더한다.
/// 서버가 켜진 화면을 찍으면 배포본이 할 수 없는 일을 할 수 있는 것처럼 오인시킨다.
/// </para>
/// <para>사용법: <c>dotnet run --project tools/Nxs.DocShots -- docs/screenshots</c></para>
/// </remarks>
public static class Program
{
    private static readonly (int Index, string Name)[] Tabs =
    [
        (0, "01-digital-io"),
        (1, "02-analog-input"),
        (2, "03-watch"),
    ];

    public static int Main(string[] args)
    {
        var outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
        Directory.CreateDirectory(outputDirectory);

        AppBuilder.Configure<Nxs.App.App>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var viewModel = BuildSampleState();
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1240,
            Height = 860,
        };
        window.Show();
        Settle(window);

        // 접속 표시등에 초록불이 들어온 상태를 찍기 위해 실제로 서버를 켜고 마스터를 붙인다.
        Nxs.TestKit.PlcTestClient? lamp = null;
        try
        {
            viewModel.Engine.ServerSettings = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 };
            viewModel.Engine.StartServerAsync().GetAwaiter().GetResult();
            lamp = Nxs.TestKit.PlcTestClient
                .ConnectAsync("127.0.0.1", viewModel.Engine.LocalEndPoint!.Port)
                .GetAwaiter().GetResult();

            for (var i = 0; i < 50 && viewModel.Engine.ConnectedClientCount == 0; i++)
            {
                Thread.Sleep(20);
            }

            viewModel.Refresh();
            Settle(window);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"접속 표시등 준비 실패(계속 진행): {ex.Message}");
        }

        var tabControl = window.GetLogicalDescendants().OfType<TabControl>().First();

        foreach (var (index, name) in Tabs)
        {
            tabControl.SelectedIndex = index;
            Settle(window);

            var path = Path.Combine(outputDirectory, name + ".png");
            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine($"렌더 실패: {name}");
                return 1;
            }

            frame.Save(path);
            Console.WriteLine($"{name}.png  ({new FileInfo(path).Length / 1024}KB)");
        }

        lamp?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        viewModel.Shutdown();

        if (!RenderTrafficLog(outputDirectory))
        {
            return 1;
        }

        Console.WriteLine($"완료 — {outputDirectory}");
        return 0;
    }

    /// <summary>
    /// 트래픽 로그 탭은 실제 왕복이 있어야 의미가 있으므로 **합성 코덱**으로 서버를 띄워 찍는다.
    /// 표시되는 hex 는 TestOnlyFrameCodec 의 합성 포맷이며 **XGT 프레임이 아니다** — README 캡션에 명시한다.
    /// </summary>
    private static bool RenderTrafficLog(string outputDirectory)
    {
        var project = NxpProject.CreateDefault(port: 0) with
        {
            Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        };

        var viewModel = new MainWindowViewModel(
            project,
            memory => new Nxs.TestKit.TestOnlyFrameCodec(new Core.Protocol.PlcRequestExecutor(memory)));

        var window = new MainWindow { DataContext = viewModel, Width = 1240, Height = 860 };
        window.Show();
        Settle(window);

        try
        {
            viewModel.Engine.StartServerAsync().GetAwaiter().GetResult();
            var port = viewModel.Engine.LocalEndPoint!.Port;

            viewModel.Engine.Memory.WriteScalar(IecAddress.Parse("%MW10"), 0xBEEF);
            viewModel.Engine.Memory.WriteWords(MemoryArea.M, 0, [0x1122, 0x3344, 0x5566]);

            Task.Run(async () =>
            {
                await using var client = await Nxs.TestKit.PlcTestClient.ConnectAsync("127.0.0.1", port);
                await client.ReadIndividualAsync("%MW10");
                await client.ReadContinuousAsync("%MW0", 6);
                await client.WriteIndividualAsync(("%QX1024", [0x01]));
                await client.ReadIndividualAsync("%IX512", "%IX513");
                // 거절 사례 하나 — 오류 행이 ErrorBrush 로 보이는 것을 함께 보여준다.
                await client.ReadContinuousAsync("%MB65530", 16);
            }).GetAwaiter().GetResult();

            viewModel.Engine.StopServerAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"트래픽 로그 스크린샷 실패: {ex.Message}");
            viewModel.Shutdown();
            return false;
        }

        viewModel.Refresh();
        Settle(window);

        var tabControl = window.GetLogicalDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = 3;
        Settle(window);

        var path = Path.Combine(outputDirectory, "04-traffic-log.png");
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("렌더 실패: 04-traffic-log");
            viewModel.Shutdown();
            return false;
        }

        frame.Save(path);
        Console.WriteLine($"04-traffic-log.png  ({new FileInfo(path).Length / 1024}KB · 합성 코덱 세션)");
        viewModel.Shutdown();
        return true;
    }

    /// <summary>레이아웃·바인딩·렌더가 안정될 때까지 UI 작업 큐를 비운다.</summary>
    private static void Settle(Window window)
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 화면이 의미 있게 보이도록 표본 상태를 만든다. 값은 코드로 생성한다(placeholder 금지).
    /// 출력 LED 는 마스터가 쓰는 영역이므로 메모리에 직접 써서 "마스터가 쓴 상태"를 재현한다.
    /// </summary>
    private static MainWindowViewModel BuildSampleState()
    {
        var scale = new AnalogChannelScale
        {
            RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
        };

        var project = NxpProject.CreateDefault(port: 2004) with
        {
            AnalogChannels = Enumerable.Range(0, 16)
                .Select(c => new AnalogChannelSettings { SlotNumber = 5, Channel = c, Scale = scale })
                .ToArray(),
            AutomationRules =
            [
                new AutomationRuleSettings
                {
                    Address = "%IW80", Kind = GeneratorKind.Sine,
                    Min = 0, Max = 10, Period = 60, PeriodMs = 100, UseEngineeringUnits = true,
                },
                new AutomationRuleSettings
                {
                    Address = "%IW81", Kind = GeneratorKind.Ramp,
                    Min = 0, Max = 100, Step = 25, PeriodMs = 500,
                },
                new AutomationRuleSettings
                {
                    Address = "%IX520", Kind = GeneratorKind.Toggle, PeriodMs = 1000,
                },
                new AutomationRuleSettings
                {
                    Address = "%MW300", Kind = GeneratorKind.Random,
                    Min = 0, Max = 4000, Seed = 7, PeriodMs = 250, IsEnabled = false,
                },
            ],
        };

        // 운영 배포와 동일하게 XGT 코덱을 주입한다 → 화면도 실제와 같은 상태
        // (서버는 켤 수 있고, 미검증 초안 경고가 뜬다).
        var viewModel = new MainWindowViewModel(
            project,
            memory => new Core.Protocol.Xgt.XgtFenetCodec(new Core.Protocol.PlcRequestExecutor(memory)));

        // 워치 목록 — LabVIEW 가 교신할 임의 주소 표본
        foreach (var (address, label, format) in new (string, string, WatchFormat)[]
        {
            ("%MW320", "설정 압력", WatchFormat.Decimal),
            ("%MD422", "적산 유량", WatchFormat.Hex),
            ("%MD500", "유량 (Float)", WatchFormat.Float),
            ("%ML60", "적산량 (Double)", WatchFormat.Double),
            ("%MX801", "운전 지령", WatchFormat.Bool),
            ("%MW501", "온도 (부호)", WatchFormat.Signed),
            ("%MB40", "상태 비트맵", WatchFormat.Binary),
        })
        {
            viewModel.NewWatchAddress = address;
            viewModel.NewWatchLabel = label;
            viewModel.AddWatchCommand.Execute(null);
            viewModel.Watches[^1].PendingFormat = format;
        }

        // 값을 먼저 넣고 형식을 나중에 적용한다 — 형식 변경이 값을 그 형식으로 다시 렌더한다.
        // 형식을 먼저 적용해야 실수 입력이 올바르게 파싱된다.
        foreach (var row in viewModel.Watches)
        {
            row.Format = row.PendingFormat;
        }

        viewModel.Watches[1].Order = ByteOrder.Abcd;   // 값보다 먼저 — 순서가 파싱에 쓰인다

        viewModel.Watches[0].ValueText = "1250";
        viewModel.Watches[1].ValueText = "0x0004D2F1";
        viewModel.Watches[2].ValueText = "12.75";
        viewModel.Watches[3].ValueText = "1234567.891011";
        viewModel.Watches[4].ValueText = "ON";
        viewModel.Watches[5].ValueText = "-125";
        viewModel.Watches[6].ValueText = "165";

        // 사용자 지정 A/D 채널
        foreach (var (address, label, rawMax, euMax, unit) in new[]
        {
            ("%IW80", "탱크 압력", "4000", "10", "bar"),
            ("%IW81", "유량", "4000", "250", "L/min"),
            ("%MW600", "노즐 온도", "4000", "400", "C"),
        })
        {
            viewModel.NewAnalogAddress = address;
            viewModel.NewAnalogLabel = label;
            viewModel.NewAnalogRawMin = "0";
            viewModel.NewAnalogRawMax = rawMax;
            viewModel.NewAnalogEuMin = "0";
            viewModel.NewAnalogEuMax = euMax;
            viewModel.NewAnalogUnit = unit;
            viewModel.AddAnalogPointCommand.Execute(null);
        }

        viewModel.AnalogPoints[0].EngineeringText = "6.25";
        viewModel.AnalogPoints[1].EngineeringText = "132.5";
        viewModel.AnalogPoints[2].RawText = "1850";

        // 사용자 지정 디지털 점 — 임의 비트 주소를 양방향으로 확인
        foreach (var (address, label) in new[]
        {
            ("%MW320", "운전 지령 워드"), ("%MX901", "리셋 요청"), ("%MB40", "모드 선택"),
        })
        {
            viewModel.NewDigitalAddress = address;
            viewModel.NewDigitalLabel = label;
            viewModel.AddDigitalPointCommand.Execute(null);
        }

        // 워드 그룹의 몇 비트를 켜 배열 표시를 보여준다
        foreach (var i in new[] { 0, 1, 3, 7, 8, 12, 15 })
        {
            viewModel.DigitalGroups[0].Bits[i].IsOn = true;
        }

        viewModel.DigitalGroups[0].Bits[0].IsOn = true;
        viewModel.DigitalGroups[2].Bits[0].IsOn = true;

        foreach (var (address, label) in new[]
        {
            ("%QW10", "운전 상태 워드 (마스터가 씀)"),
        })
        {
            viewModel.NewDigitalAddress = address;
            viewModel.NewDigitalLabel = label;
            viewModel.AddDigitalPointCommand.Execute(null);
        }

        // 마스터가 쓴 상태 모사 — 같은 목록에서 함께 보인다
        viewModel.Engine.Memory.WriteScalar(IecAddress.Parse("%QW10"), 0b1000_0001_0010_0101);
        viewModel.Refresh();

        // 입력 점: 결정적 패턴 (3의 배수 ON)
        foreach (var slot in viewModel.InputSlots)
        {
            foreach (var point in slot.Points.Where(p => p.PointNumber % 3 == 0))
            {
                point.IsOn = true;
            }
        }

        // 출력 LED: 마스터가 쓴 값 — 메모리에 직접 기록
        var outputs = viewModel.OutputSlots[0];
        foreach (var point in outputs.Points.Where(p => p.PointNumber % 4 is 0 or 1))
        {
            viewModel.Engine.Memory.WriteBit(point.Address, true);
        }

        viewModel.Refresh();
        return viewModel;
    }

    private static IEnumerable<Control> GetLogicalDescendants(this Window window)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<Control>();
}
