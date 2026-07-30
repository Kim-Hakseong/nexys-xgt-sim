using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Time;

namespace Nxs.Core.Automation;

/// <summary>
/// 룰 엔진 (PRD X-06) — 주기가 도래한 룰의 제너레이터 값을 메모리에 쓴다.
/// </summary>
/// <remarks>
/// <para>
/// tick 인덱스는 룰별로 독립이며 <see cref="ITimeSource.MonotonicMilliseconds"/> 기준으로 진행한다
/// (월클럭 점프에 영향받지 않는다). 제너레이터가 순수 함수이므로 엔진 상태는 "룰별 다음 실행 시각 +
/// tick 인덱스"뿐이다.
/// </para>
/// <para>
/// 한 룰의 실패가 다른 룰을 막지 않는다 — 범위 밖 주소 같은 실수는 보고하고 계속 진행한다.
/// </para>
/// </remarks>
public sealed class AutomationEngine
{
    private readonly PlcMemory _memory;
    private readonly ITimeSource _time;
    private readonly List<RuleState> _states;

    /// <summary>엔진을 만든다.</summary>
    /// <exception cref="ArgumentException">룰 주기가 0 이하일 때.</exception>
    public AutomationEngine(PlcMemory memory, ITimeSource timeSource, IEnumerable<AutomationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(timeSource);
        ArgumentNullException.ThrowIfNull(rules);

        _memory = memory;
        _time = timeSource;
        _states = [];

        foreach (var rule in rules)
        {
            rule.Validate();
            _states.Add(new RuleState(rule));
        }
    }

    /// <summary>등록된 룰.</summary>
    public IReadOnlyList<AutomationRule> Rules => _states.Select(s => s.Rule).ToArray();

    /// <summary>
    /// 룰의 사용 여부를 실행 중에 바꾼다 (UI 체크박스). 다시 켤 때 tick 인덱스는 유지된다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">인덱스가 범위를 벗어났을 때.</exception>
    public void SetEnabled(int ruleIndex, bool enabled)
    {
        if (ruleIndex < 0 || ruleIndex >= _states.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ruleIndex), ruleIndex, $"룰 인덱스는 0..{_states.Count - 1} 범위여야 합니다");
        }

        _states[ruleIndex].IsEnabled = enabled;
    }

    /// <summary>룰이 현재 켜져 있는지.</summary>
    public bool IsEnabled(int ruleIndex) => _states[ruleIndex].IsEnabled;

    /// <summary>
    /// 주기가 도래한 룰을 적용한다. 호출 시각은 주입된 시간 원천에서 읽는다.
    /// </summary>
    public AutomationTickResult Tick()
    {
        var now = _time.MonotonicMilliseconds;
        var applied = 0;
        List<string>? failures = null;

        foreach (var state in _states)
        {
            if (!state.IsEnabled)
            {
                continue;
            }

            if (state.NextDueMs is { } due && now < due)
            {
                continue;
            }

            var periodMs = (long)Math.Max(1, state.Rule.Period.TotalMilliseconds);
            state.NextDueMs = now + periodMs;

            try
            {
                Apply(state.Rule, state.TickIndex);
                applied++;
            }
            catch (Exception ex) when (ex is AddressRangeException or ArgumentException or InvalidOperationException)
            {
                (failures ??= []).Add($"{state.Rule.Target.Text}: {ex.Message}");
            }

            state.TickIndex = state.TickIndex == int.MaxValue ? 0 : state.TickIndex + 1;
        }

        return new AutomationTickResult(applied, failures?.Count ?? 0, failures ?? []);
    }

    /// <summary>모든 룰의 tick 인덱스와 주기 상태를 초기화한다.</summary>
    public void Reset()
    {
        foreach (var state in _states)
        {
            state.TickIndex = 0;
            state.NextDueMs = null;
        }
    }

    /// <summary>취소될 때까지 주기적으로 <see cref="Tick"/>을 호출한다.</summary>
    /// <param name="resolution">tick 검사 간격.</param>
    /// <param name="cancellationToken">취소 토큰.</param>
    public async Task RunAsync(TimeSpan resolution, CancellationToken cancellationToken)
    {
        if (resolution <= TimeSpan.Zero)
        {
            throw new ArgumentException($"resolution 은 0보다 커야 합니다. 실제: {resolution}", nameof(resolution));
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Tick();
                await _time.Delay(resolution, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료 경로.
        }
    }

    private void Apply(AutomationRule rule, int tickIndex)
    {
        var generated = rule.Generator.ValueAt(tickIndex);

        if (rule.Target.Size == DataSize.Bit)
        {
            _memory.WriteBit(rule.Target, generated != 0);
            return;
        }

        // 스케일이 있으면 제너레이터 출력은 공학단위다 → raw 로 변환한다.
        var raw = rule.Scale is { } scale
            ? AnalogChannelScale.RawToWord(scale.ToRaw(generated))
            : unchecked((ushort)generated);

        _memory.WriteScalar(rule.Target, rule.Target.Size == DataSize.Byte ? (byte)raw : raw);
    }

    private sealed class RuleState(AutomationRule rule)
    {
        internal AutomationRule Rule { get; } = rule;

        /// <summary>실행 중 변경 가능한 사용 여부. 초기값은 룰 정의를 따른다.</summary>
        internal bool IsEnabled { get; set; } = rule.IsEnabled;

        internal int TickIndex { get; set; }

        internal long? NextDueMs { get; set; }
    }
}
