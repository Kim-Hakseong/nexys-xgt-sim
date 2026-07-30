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
        (0, "01-digital-input"),
        (1, "02-digital-output"),
        (2, "03-analog-input"),
        (3, "04-automation"),
        (5, "06-rack-config"),
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
        tabControl.SelectedIndex = 4;
        Settle(window);

        var path = Path.Combine(outputDirectory, "05-traffic-log.png");
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("렌더 실패: 05-traffic-log");
            viewModel.Shutdown();
            return false;
        }

        frame.Save(path);
        Console.WriteLine($"05-traffic-log.png  ({new FileInfo(path).Length / 1024}KB · 합성 코덱 세션)");
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

        var viewModel = new MainWindowViewModel(project);

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

        // AD 채널: 채널마다 다른 공학단위 값
        for (var c = 0; c < viewModel.AnalogSlots[0].Channels.Count; c++)
        {
            var channel = viewModel.AnalogSlots[0].Channels[c];
            channel.EngineeringText = (c * 0.625).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        for (var c = 0; c < viewModel.AnalogSlots[1].Channels.Count; c++)
        {
            viewModel.Engine.Memory.WriteScalar(
                viewModel.AnalogSlots[1].Channels[c].Address, (uint)(1000 + (c * 137)));
        }

        viewModel.Refresh();
        return viewModel;
    }

    private static IEnumerable<Control> GetLogicalDescendants(this Window window)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<Control>();
}
