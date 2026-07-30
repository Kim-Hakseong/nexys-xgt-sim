using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nxs.App.ViewModels;
using Nxs.Core.Configuration;

namespace Nxs.App.Views;

/// <summary>메인 윈도우.</summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>윈도우를 만든다.</summary>
    public MainWindow()
    {
        InitializeComponent();

        // 마스터가 쓴 %Q 값과 자동화 결과를 화면에 끌어오기 위한 주기 갱신.
        // 메모리 읽기만 하므로 UI 스레드를 블로킹하지 않는다 (CLAUDE.md §3).
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _refreshTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.Refresh();
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _refreshTimer.Start();
    }

    /// <inheritdoc />
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnUnloaded(e);
    }

    private static readonly FilePickerFileType NxpFileType = new("Nexys XGT 프로젝트")
    {
        Patterns = ["*" + NxpProjectFile.Extension],
    };

    private static readonly FilePickerFileType LogFileType = new("텍스트 로그")
    {
        Patterns = ["*.log", "*.txt"],
    };

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "프로젝트 열기",
            AllowMultiple = false,
            FileTypeFilter = [NxpFileType],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.OpenProject(path);
        }
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "프로젝트 저장",
            SuggestedFileName = "rack" + NxpProjectFile.Extension,
            DefaultExtension = NxpProjectFile.Extension.TrimStart('.'),
            FileTypeChoices = [NxpFileType],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            vm.SaveProject(path);
        }
    }

    private async void OnSaveTrafficClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "트래픽 로그 저장",
            SuggestedFileName = "traffic.log",
            DefaultExtension = "log",
            FileTypeChoices = [LogFileType],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            vm.SaveTraffic(path);
        }
    }
}
