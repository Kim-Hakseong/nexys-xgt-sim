using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Automation;

/// <summary>
/// 자동화 룰의 직렬화 형태 (.nxp 저장용 — PRD X-08).
/// </summary>
/// <remarks>
/// 제너레이터는 다형 타입이라 JSON 다형 직렬화 대신 <see cref="Kind"/> + 평평한 파라미터로 담는다.
/// 사람이 .nxp 를 열어 고칠 수 있어야 하므로 구조를 단순하게 유지한다.
/// </remarks>
public sealed record AutomationRuleSettings
{
    /// <summary>대상 주소 표기.</summary>
    public required string Address { get; init; }

    /// <summary>제너레이터 종류.</summary>
    public required GeneratorKind Kind { get; init; }

    /// <summary>적용 주기(밀리초).</summary>
    public int PeriodMs { get; init; } = 1000;

    /// <summary>최소값 / 고정값.</summary>
    public double Min { get; init; }

    /// <summary>최대값.</summary>
    public double Max { get; init; } = 65535;

    /// <summary>tick 당 증가량 (Increment/Ramp).</summary>
    public int Step { get; init; } = 1;

    /// <summary>한 주기의 tick 수 (Sine).</summary>
    public int Period { get; init; } = 60;

    /// <summary>시드 (Random).</summary>
    public int Seed { get; init; }

    /// <summary>사용 여부.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 참이면 제너레이터 출력을 공학단위로 해석해 채널 스케일로 raw 변환한다.
    /// </summary>
    public bool UseEngineeringUnits { get; init; }

    /// <summary>공학단위 변환에 쓸 스케일. null이면 프로젝트의 채널 설정에서 찾는다.</summary>
    public AnalogChannelScale? Scale { get; init; }

    /// <summary>실행 가능한 룰로 변환한다.</summary>
    /// <param name="addressing">주소 산법 설정.</param>
    /// <param name="scaleLookup">주소별 채널 스케일 조회. 없으면 null 반환.</param>
    /// <exception cref="FormatException">주소 표기를 해석할 수 없을 때.</exception>
    /// <exception cref="ArgumentException">파라미터가 유효하지 않을 때.</exception>
    public AutomationRule ToRule(
        AddressingOptions? addressing = null,
        Func<IecAddress, AnalogChannelScale?>? scaleLookup = null)
    {
        var target = IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);

        var rule = new AutomationRule
        {
            Target = target,
            Generator = BuildGenerator(),
            Period = TimeSpan.FromMilliseconds(PeriodMs),
            IsEnabled = IsEnabled,
            Scale = UseEngineeringUnits ? Scale ?? scaleLookup?.Invoke(target) : null,
        };

        rule.Validate();
        return rule;
    }

    /// <summary>실행 룰에서 직렬화 형태를 만든다.</summary>
    public static AutomationRuleSettings FromRule(AutomationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var settings = new AutomationRuleSettings
        {
            Address = rule.Target.Text,
            Kind = rule.Generator.Kind,
            PeriodMs = (int)rule.Period.TotalMilliseconds,
            IsEnabled = rule.IsEnabled,
            UseEngineeringUnits = rule.Scale is not null,
            Scale = rule.Scale,
        };

        return rule.Generator switch
        {
            FixedGenerator g => settings with { Min = g.Value, Max = g.Value },
            IncrementGenerator g => settings with { Min = g.Min, Max = g.Max, Step = g.Step },
            RampGenerator g => settings with { Min = g.Min, Max = g.Max, Step = g.Step },
            SineGenerator g => settings with { Min = g.Min, Max = g.Max, Period = g.Period },
            RandomGenerator g => settings with { Min = g.Min, Max = g.Max, Seed = g.Seed },
            ToggleGenerator => settings,
            _ => settings,
        };
    }

    private IValueGenerator BuildGenerator()
    {
        var min = (int)Math.Round(Min, MidpointRounding.AwayFromZero);
        var max = (int)Math.Round(Max, MidpointRounding.AwayFromZero);

        return Kind switch
        {
            GeneratorKind.Fixed => new FixedGenerator { Value = min },
            GeneratorKind.Increment => new IncrementGenerator { Min = min, Max = max, Step = Step },
            GeneratorKind.Ramp => new RampGenerator { Min = min, Max = max, Step = Step },
            GeneratorKind.Sine => new SineGenerator { Min = min, Max = max, Period = Period },
            GeneratorKind.Random => new RandomGenerator { Min = min, Max = max, Seed = Seed },
            GeneratorKind.Toggle => new ToggleGenerator(),
            _ => throw new ArgumentException($"알 수 없는 제너레이터 종류: {Kind}", nameof(Kind)),
        };
    }
}
