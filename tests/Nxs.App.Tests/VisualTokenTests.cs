using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nxs.App.ViewModels;
using Nxs.App.Views;
using Xunit;

namespace Nxs.App.Tests;

/// <summary>
/// DESIGN.md Rev.B "비주얼 이식 검증" 골든 벡터 — 수정/삭제 금지.
/// 원본(nexys-modbus-workbench App.axaml)에서 추출한 토큰 값과 전 항목 일치해야 한다.
/// </summary>
public class VisualTokenTests
{
    private static T Resource<T>(string key)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(
            app.TryFindResource(key, ThemeVariant.Light, out var value),
            $"리소스 '{key}'를 찾을 수 없습니다");
        return Assert.IsType<T>(value);
    }

    private static Color Brush(string key) => Resource<SolidColorBrush>(key).Color;

    private static Color Hex(string hex) => Color.Parse(hex);

    [AvaloniaTheory]
    [InlineData("AccentBrush", "#7A1020")]
    [InlineData("AccentSoftBrush", "#F0EAE7")]
    [InlineData("CardBrush", "#FCFBF9")]
    [InlineData("CardSoftBrush", "#F1EFEA")]
    [InlineData("LineBrush", "#DDDBD3")]
    [InlineData("InkBrush", "#16171A")]
    [InlineData("InkHoverBrush", "#2C2E33")]
    [InlineData("TextPrimaryBrush", "#16171A")]
    [InlineData("TextSecondaryBrush", "#8B897F")]
    [InlineData("ErrorBrush", "#9C2030")]
    public void PaletteTokenMatchesTheDesignTable(string key, string expected)
        => Assert.Equal(Hex(expected), Brush(key));

    [AvaloniaTheory]
    [InlineData("ToggleButtonBackgroundChecked", "#7A1020")]
    [InlineData("ToggleButtonBackgroundCheckedPointerOver", "#9C2030")]
    [InlineData("ToggleButtonBackgroundCheckedPressed", "#5A0B18")]
    [InlineData("ToggleButtonForegroundChecked", "#FFFFFF")]
    public void ToggleCheckedStateIsWineRedWithWhiteText(string key, string expected)
        => Assert.Equal(Hex(expected), Brush(key));

    [AvaloniaFact]
    public void AccentColorFamilyMatchesTheDesignTable()
    {
        Assert.Equal(Hex("#7A1020"), Resource<Color>("SystemAccentColor"));
        Assert.Equal(Hex("#5A0B18"), Resource<Color>("SystemAccentColorDark2"));
        Assert.Equal(Hex("#9C2030"), Resource<Color>("SystemAccentColorLight1"));
    }

    [AvaloniaFact]
    public void DataDisplayControlsUseMonospace()
    {
        // DESIGN: 데이터(주소·hex·값·프레임)는 예외 없이 Menlo,Consolas,monospace.
        var window = new Window();
        var block = new TextBlock { Classes = { "mono" } };
        var box = new TextBox { Classes = { "mono" } };
        var panel = new StackPanel { Children = { block, box } };
        window.Content = panel;
        window.Show();

        foreach (var family in new[] { block.FontFamily.ToString(), box.FontFamily.ToString() })
        {
            Assert.Contains("monospace", family, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Menlo", family, StringComparison.Ordinal);
            Assert.Contains("Consolas", family, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void AddressAndValueControlsInTheRealWindowAreMonospace()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        var monoControls = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Classes.Contains("mono"))
            .ToList();

        Assert.NotEmpty(monoControls);
        foreach (var control in monoControls)
        {
            var family = control switch
            {
                TextBlock t => t.FontFamily.ToString(),
                TextBox t => t.FontFamily.ToString(),
                ToggleButton t => t.FontFamily.ToString(),
                _ => "monospace",
            };
            Assert.Contains("monospace", family, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public void PillStyleClassIsAvailableAndReusesLineBrushBorder()
    {
        var window = new Window();
        var pill = new Border { Classes = { "pill" } };
        window.Content = pill;
        window.Show();

        Assert.Equal(new CornerRadius(17), pill.CornerRadius);
        Assert.Equal(Hex("#DDDBD3"), Assert.IsType<SolidColorBrush>(pill.BorderBrush).Color);
    }

    [AvaloniaFact]
    public void OutputLedUsesOnlyAccentAndLineBrushNoNewColours()
    {
        // DESIGN: LED ON = 와인레드 채움, OFF = LineBrush 테두리 빈 원. 팔레트 외 색(녹색 등) 금지.
        var window = new Window();
        var off = new Avalonia.Controls.Shapes.Ellipse { Classes = { "led" } };
        var on = new Avalonia.Controls.Shapes.Ellipse { Classes = { "led", "on" } };
        window.Content = new StackPanel { Children = { off, on } };
        window.Show();

        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(off.Fill).Color);
        Assert.Equal(Hex("#DDDBD3"), Assert.IsAssignableFrom<ISolidColorBrush>(off.Stroke).Color);
        Assert.Equal(Hex("#7A1020"), Assert.IsAssignableFrom<ISolidColorBrush>(on.Fill).Color);
    }

    [AvaloniaFact]
    public void DisabledAccentButtonDoesNotLookClickable()
    {
        // ⛔ 게이트로 잠긴 [시작] 버튼이 활성 상태처럼 보이면 사용자가 계속 누르게 된다.
        var window = new Window();
        var enabled = new Button { Classes = { "accent" }, Content = "시작" };
        var disabled = new Button { Classes = { "accent" }, Content = "시작", IsEnabled = false };
        window.Content = new StackPanel { Children = { enabled, disabled } };
        window.Show();

        var enabledFill = PresenterBackground(enabled);
        var disabledFill = PresenterBackground(disabled);

        Assert.Equal(Hex("#16171A"), enabledFill);
        Assert.Equal(Hex("#F1EFEA"), disabledFill);
        Assert.NotEqual(enabledFill, disabledFill);
    }

    [AvaloniaFact]
    public void StartButtonIsDisabledWhenTheCodecGateIsClosed()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var startButton = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("accent"));

        Assert.False(vm.CanStartServer);
        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.Equal(Hex("#F1EFEA"), PresenterBackground(startButton));

        vm.Shutdown();
    }

    private static Color PresenterBackground(Button button)
    {
        var presenter = button.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(p => string.Equals(p.Name, "PART_ContentPresenter", StringComparison.Ordinal));
        return Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color;
    }

    [AvaloniaFact]
    public void WindowUsesTheWarmNeutralGradientBackground()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        var gradient = Assert.IsType<LinearGradientBrush>(window.Background);
        Assert.Equal(Hex("#F4F3F0"), gradient.GradientStops[0].Color);
        Assert.Equal(Hex("#ECEAE5"), gradient.GradientStops[1].Color);
        Assert.Equal(Hex("#DDDBD3"), gradient.GradientStops[2].Color);
    }
}
