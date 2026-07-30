using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.TestKit;

namespace Nxs.Core.Tests.Automation;

/// <summary>
/// 룰 엔진 (PRD X-06) — 룰=(대상 주소, 제너레이터, 주기). 시간은 ITimeSource 주입(CLAUDE.md §4.2).
/// </summary>
public class AutomationEngineTests
{
    private static (AutomationEngine Engine, PlcMemory Memory, FakeTimeSource Time) New(params AutomationRule[] rules)
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 4096 });
        var time = new FakeTimeSource();
        return (new AutomationEngine(memory, time, rules), memory, time);
    }

    private static AutomationRule Rule(string address, IValueGenerator generator, int periodMs = 100)
        => new()
        {
            Target = IecAddress.Parse(address),
            Generator = generator,
            Period = TimeSpan.FromMilliseconds(periodMs),
        };

    [Fact]
    public void FirstTickWritesTickZeroValue()
    {
        var (engine, memory, _) = New(Rule("%MW0", new RampGenerator { Min = 0, Max = 100, Step = 25 }));

        engine.Tick();

        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void SuccessiveDuePeriodsWalkTheGoldenRampVector()
    {
        var (engine, memory, time) = New(Rule("%MW0", new RampGenerator { Min = 0, Max = 100, Step = 25 }));
        var observed = new List<uint>();

        for (var i = 0; i < 6; i++)
        {
            engine.Tick();
            observed.Add(memory.ReadScalar(IecAddress.Parse("%MW0")));
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(new uint[] { 0, 25, 50, 75, 100, 0 }, observed);
    }

    [Fact]
    public void RuleDoesNotAdvanceBeforeItsPeriodElapses()
    {
        var (engine, memory, time) = New(Rule("%MW0", new IncrementGenerator { Min = 0, Max = 999, Step = 1 }));

        engine.Tick();
        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));

        time.Advance(TimeSpan.FromMilliseconds(50));
        engine.Tick();
        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));

        time.Advance(TimeSpan.FromMilliseconds(50));
        engine.Tick();
        Assert.Equal(1u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void RulesWithDifferentPeriodsAdvanceIndependently()
    {
        var fast = Rule("%MW0", new IncrementGenerator { Min = 0, Max = 999, Step = 1 }, periodMs: 100);
        var slow = Rule("%MW1", new IncrementGenerator { Min = 0, Max = 999, Step = 1 }, periodMs: 300);
        var (engine, memory, time) = New(fast, slow);

        for (var ms = 0; ms <= 600; ms += 100)
        {
            engine.Tick();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        // 0..600ms 에 100ms 룰은 tick 0..6, 300ms 룰은 tick 0..2
        Assert.Equal(6u, memory.ReadScalar(IecAddress.Parse("%MW0")));
        Assert.Equal(2u, memory.ReadScalar(IecAddress.Parse("%MW1")));
    }

    [Fact]
    public void ToggleRuleOnABitAddressWritesTheBit()
    {
        var (engine, memory, time) = New(Rule("%MX100", new ToggleGenerator()));

        engine.Tick();
        Assert.True(memory.ReadBit(IecAddress.Parse("%MX100")));

        time.Advance(TimeSpan.FromMilliseconds(100));
        engine.Tick();
        Assert.False(memory.ReadBit(IecAddress.Parse("%MX100")));

        time.Advance(TimeSpan.FromMilliseconds(100));
        engine.Tick();
        Assert.True(memory.ReadBit(IecAddress.Parse("%MX100")));
    }

    [Fact]
    public void EngineeringUnitRuleConvertsThroughTheChannelScale()
    {
        // DESIGN: AD 채널은 공학단위 룰(min/max) → raw 변환(채널 설정의 스케일 공유).
        var scale = new AnalogChannelScale
        {
            RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
        };
        var rule = new AutomationRule
        {
            Target = IecAddress.Parse("%IW80"),
            Generator = new RampGenerator { Min = 0, Max = 10, Step = 5 },
            Period = TimeSpan.FromMilliseconds(100),
            Scale = scale,
        };
        var (engine, memory, time) = New(rule);

        var observed = new List<uint>();
        for (var i = 0; i < 3; i++)
        {
            engine.Tick();
            observed.Add(memory.ReadScalar(IecAddress.Parse("%IW80")));
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        // 제너레이터가 공학단위 0,5,10 을 내면 스케일이 raw 0,2000,4000 으로 바꾼다.
        Assert.Equal(new uint[] { 0, 2000, 4000 }, observed);
    }

    [Fact]
    public void DisabledRuleDoesNothing()
    {
        var rule = Rule("%MW0", new FixedGenerator { Value = 999 }) with { IsEnabled = false };
        var (engine, memory, _) = New(rule);

        engine.Tick();

        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void OutOfRangeTargetIsReportedNotThrown()
    {
        var (engine, _, _) = New(Rule("%MW9999", new FixedGenerator { Value = 1 }));

        var result = engine.Tick();

        Assert.Equal(1, result.FailedCount);
        Assert.Contains("%MW9999", Assert.Single(result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void OneBadRuleDoesNotStopTheOthers()
    {
        var (engine, memory, _) = New(
            Rule("%MW9999", new FixedGenerator { Value = 1 }),
            Rule("%MW5", new FixedGenerator { Value = 77 }));

        var result = engine.Tick();

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(77u, memory.ReadScalar(IecAddress.Parse("%MW5")));
    }

    [Fact]
    public void TickReportsHowManyRulesFired()
    {
        var (engine, _, time) = New(
            Rule("%MW0", new FixedGenerator { Value = 1 }, periodMs: 100),
            Rule("%MW1", new FixedGenerator { Value = 2 }, periodMs: 500));

        Assert.Equal(2, engine.Tick().AppliedCount);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, engine.Tick().AppliedCount);
    }

    [Fact]
    public void ResetRestartsTickIndicesFromZero()
    {
        var (engine, memory, time) = New(Rule("%MW0", new IncrementGenerator { Min = 0, Max = 999, Step = 1 }));

        engine.Tick();
        time.Advance(TimeSpan.FromMilliseconds(100));
        engine.Tick();
        Assert.Equal(1u, memory.ReadScalar(IecAddress.Parse("%MW0")));

        engine.Reset();
        engine.Tick();

        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public async Task RunAsyncAppliesRulesUntilCancelled()
    {
        var (engine, memory, _) = New(Rule("%MW0", new FixedGenerator { Value = 7 }));
        using var cts = new CancellationTokenSource();

        var run = engine.RunAsync(TimeSpan.FromMilliseconds(100), cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (memory.ReadScalar(IecAddress.Parse("%MW0")) != 7 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.Equal(7u, memory.ReadScalar(IecAddress.Parse("%MW0")));

        await cts.CancelAsync();
        await run;

        // 취소되면 루프가 반드시 끝난다(RunAsync 는 OperationCanceledException 을 삼킨다).
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public void ZeroPeriodRuleIsRejectedAtConstruction()
    {
        var bad = new AutomationRule
        {
            Target = IecAddress.Parse("%MW0"),
            Generator = new FixedGenerator { Value = 1 },
            Period = TimeSpan.Zero,
        };

        Assert.Throws<ArgumentException>(() => New(bad));
    }

    [Fact]
    public void RuleCanBeDisabledAndReEnabledAtRuntime()
    {
        var (engine, memory, time) = New(Rule("%MW0", new IncrementGenerator { Min = 0, Max = 999, Step = 1 }));

        engine.Tick();
        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));

        engine.SetEnabled(0, false);
        Assert.False(engine.IsEnabled(0));
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, engine.Tick().AppliedCount);

        engine.SetEnabled(0, true);
        time.Advance(TimeSpan.FromMilliseconds(100));
        engine.Tick();

        // 다시 켜면 tick 인덱스가 유지된 채 이어진다.
        Assert.Equal(1u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void SetEnabledRejectsAnOutOfRangeIndex()
    {
        var (engine, _, _) = New(Rule("%MW0", new ToggleGenerator()));

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetEnabled(1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetEnabled(-1, false));
    }

    [Fact]
    public void EngineWithNoRulesTicksHarmlessly()
    {
        var (engine, _, _) = New();

        var result = engine.Tick();

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(0, result.FailedCount);
    }
}
