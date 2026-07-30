using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Automation;

/// <summary>
/// 자동화 룰 = (대상 주소, 제너레이터, 주기) — DESIGN.
/// </summary>
public sealed record AutomationRule
{
    /// <summary>대상 주소.</summary>
    public required IecAddress Target { get; init; }

    /// <summary>값 제너레이터.</summary>
    public required IValueGenerator Generator { get; init; }

    /// <summary>적용 주기.</summary>
    public required TimeSpan Period { get; init; }

    /// <summary>
    /// 공학단위 스케일. 지정하면 제너레이터 출력을 **공학단위로 해석**해 raw 로 변환한 뒤 쓴다
    /// (AD 채널 룰 — 채널 설정의 스케일을 공유한다).
    /// </summary>
    public AnalogChannelScale? Scale { get; init; }

    /// <summary>사용 여부.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>표시 이름(UI·로그용).</summary>
    public string DisplayName => $"{Target.Text} · {Generator.Kind} · {Period.TotalMilliseconds:0}ms";

    /// <summary>룰이 유효한지 검사한다.</summary>
    /// <exception cref="ArgumentException">주기가 0 이하일 때.</exception>
    public void Validate()
    {
        if (Period <= TimeSpan.Zero)
        {
            throw new ArgumentException($"룰 주기는 0보다 커야 합니다. 실제: {Period}", nameof(Period));
        }
    }
}

/// <summary>한 번의 tick 결과.</summary>
/// <param name="AppliedCount">적용된 룰 수.</param>
/// <param name="FailedCount">실패한 룰 수.</param>
/// <param name="Failures">실패 설명.</param>
public sealed record AutomationTickResult(int AppliedCount, int FailedCount, IReadOnlyList<string> Failures);
