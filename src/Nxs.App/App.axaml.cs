using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Nxs.Core.Fixtures;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;

namespace Nxs.App;

/// <summary>애플리케이션 진입 클래스.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // XGT FEnet 코덱을 연결한다. ⚠️ 초안 구현이므로 UI 가 미검증 경고를 표시한다
            // (XgtFenetCodec.IsDraft). 수신 프레임은 fixtures/labview-capture/ 에 자동 캡처해
            // spec 검증 근거로 남긴다 — 마스터를 한 번 붙이면 검증 자료가 모인다.
            var captureDirectory = CaptureFixtureLoader.FindDirectory()
                ?? Path.Combine(AppContext.BaseDirectory, CaptureFixtureLoader.DefaultRelativePath);

            var viewModel = new MainWindowViewModel(
                codecFactory: memory => new XgtFenetCodec(new PlcRequestExecutor(memory)),
                frameRecorder: new FrameRecorder(captureDirectory));
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
