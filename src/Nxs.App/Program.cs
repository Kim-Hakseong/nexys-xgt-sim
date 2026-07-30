using Avalonia;

namespace Nxs.App;

/// <summary>프로세스 진입점.</summary>
public static class Program
{
    /// <summary>애플리케이션을 시작한다.</summary>
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Avalonia 앱 빌더를 구성한다. 헤드리스 테스트도 이 구성을 공유한다.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
