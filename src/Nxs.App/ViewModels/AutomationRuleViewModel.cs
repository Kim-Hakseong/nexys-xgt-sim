using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Automation;

namespace Nxs.App.ViewModels;

/// <summary>자동화 룰 한 개의 표시 모델 (PRD X-06).</summary>
public sealed partial class AutomationRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>표시 모델을 만든다.</summary>
    public AutomationRuleViewModel(AutomationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Rule = rule;
        _isEnabled = rule.IsEnabled;
    }

    /// <summary>원본 룰.</summary>
    public AutomationRule Rule { get; }

    /// <summary>대상 주소 표기.</summary>
    public string AddressText => Rule.Target.Text;

    /// <summary>제너레이터 종류 표기.</summary>
    public string KindText => Rule.Generator.Kind switch
    {
        GeneratorKind.Fixed => "고정",
        GeneratorKind.Increment => "증가",
        GeneratorKind.Ramp => "램프",
        GeneratorKind.Sine => "사인",
        GeneratorKind.Random => "랜덤",
        GeneratorKind.Toggle => "토글",
        _ => Rule.Generator.Kind.ToString(),
    };

    /// <summary>주기 표기.</summary>
    public string PeriodText
        => $"{Rule.Period.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";

    /// <summary>공학단위 스케일 표기. 없으면 "raw".</summary>
    public string ScaleText => Rule.Scale is { } s
        ? $"{s.EngineeringMin:0.##}~{s.EngineeringMax:0.##} {s.Unit} → raw {s.RawMin}~{s.RawMax}"
        : "raw";

    /// <summary>미리보기 — tick 0..7 값.</summary>
    public string PreviewText
        => string.Join(", ", Enumerable.Range(0, 8).Select(t => Rule.Generator.ValueAt(t)));
}
