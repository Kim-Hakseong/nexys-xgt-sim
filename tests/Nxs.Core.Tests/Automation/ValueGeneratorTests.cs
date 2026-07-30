using Nxs.Core.Automation;

namespace Nxs.Core.Tests.Automation;

/// <summary>
/// DESIGN.md 골든 벡터 — 자동화 (검증된 값). 수정/삭제 금지.
/// Ramp(0,100,25): tick0..5 → 0,25,50,75,100,0 · Sine(0,1000,period4): 500,1000,500,0 · Toggle: T,F,T
/// 제너레이터는 tickIndex 의 순수 함수여야 한다.
/// </summary>
public class ValueGeneratorTests
{
    [Fact]
    public void RampGoldenVector()
    {
        var ramp = new RampGenerator { Min = 0, Max = 100, Step = 25 };

        Assert.Equal(new[] { 0, 25, 50, 75, 100, 0 }, Ticks(ramp, 6));
    }

    [Fact]
    public void SineGoldenVector()
    {
        var sine = new SineGenerator { Min = 0, Max = 1000, Period = 4 };

        Assert.Equal(new[] { 500, 1000, 500, 0 }, Ticks(sine, 4));
    }

    [Fact]
    public void ToggleGoldenVector()
    {
        var toggle = new ToggleGenerator();

        Assert.Equal(new[] { 1, 0, 1 }, Ticks(toggle, 3));
    }

    [Fact]
    public void SineRepeatsAfterItsPeriod()
    {
        var sine = new SineGenerator { Min = 0, Max = 1000, Period = 4 };

        Assert.Equal(Ticks(sine, 4), Ticks(sine, 8).Skip(4).ToArray());
    }

    [Fact]
    public void RampRepeatsAfterItsCycle()
    {
        var ramp = new RampGenerator { Min = 0, Max = 100, Step = 25 };

        Assert.Equal(Ticks(ramp, 5), Ticks(ramp, 10).Skip(5).ToArray());
    }

    [Fact]
    public void FixedGeneratorAlwaysReturnsTheSameValue()
    {
        var fixedGen = new FixedGenerator { Value = 1234 };

        Assert.Equal(new[] { 1234, 1234, 1234 }, Ticks(fixedGen, 3));
    }

    [Fact]
    public void IncrementGeneratorCountsUpByStepAndWrapsAtMax()
    {
        var inc = new IncrementGenerator { Min = 10, Max = 13, Step = 1 };

        Assert.Equal(new[] { 10, 11, 12, 13, 10, 11 }, Ticks(inc, 6));
    }

    [Fact]
    public void IncrementGeneratorHonoursStepLargerThanOne()
    {
        var inc = new IncrementGenerator { Min = 0, Max = 9, Step = 4 };

        // 0, 4, 8, 12%10=2, 16%10=6, 20%10=0
        Assert.Equal(new[] { 0, 4, 8, 2, 6, 0 }, Ticks(inc, 6));
    }

    [Fact]
    public void RandomGeneratorIsAPureFunctionOfTickSoItIsReproducible()
    {
        var a = new RandomGenerator { Min = 0, Max = 1000, Seed = 42 };
        var b = new RandomGenerator { Min = 0, Max = 1000, Seed = 42 };

        Assert.Equal(Ticks(a, 20), Ticks(b, 20));
    }

    [Fact]
    public void RandomGeneratorStaysWithinRange()
    {
        var random = new RandomGenerator { Min = -50, Max = 50, Seed = 7 };

        foreach (var value in Ticks(random, 500))
        {
            Assert.InRange(value, -50, 50);
        }
    }

    [Fact]
    public void RandomGeneratorWithDifferentSeedsDiffers()
    {
        var a = new RandomGenerator { Min = 0, Max = 10000, Seed = 1 };
        var b = new RandomGenerator { Min = 0, Max = 10000, Seed = 2 };

        Assert.NotEqual(Ticks(a, 20), Ticks(b, 20));
    }

    [Fact]
    public void RandomGeneratorActuallyVaries()
    {
        var random = new RandomGenerator { Min = 0, Max = 1000, Seed = 3 };

        Assert.True(Ticks(random, 50).Distinct().Count() > 10, "난수가 사실상 고정되어 있습니다");
    }

    [Fact]
    public void GeneratorsAreStatelessSoOutOfOrderTicksStillMatch()
    {
        var ramp = new RampGenerator { Min = 0, Max = 100, Step = 25 };

        Assert.Equal(75, ramp.ValueAt(3));
        Assert.Equal(0, ramp.ValueAt(0));
        Assert.Equal(75, ramp.ValueAt(3));
        Assert.Equal(100, ramp.ValueAt(4));
    }

    [Fact]
    public void RampWithZeroStepIsRejected()
        => Assert.Throws<ArgumentException>(
            () => new RampGenerator { Min = 0, Max = 100, Step = 0 }.ValueAt(0));

    [Fact]
    public void SineWithZeroPeriodIsRejected()
        => Assert.Throws<ArgumentException>(
            () => new SineGenerator { Min = 0, Max = 100, Period = 0 }.ValueAt(0));

    [Fact]
    public void RampWithInvertedRangeIsRejected()
        => Assert.Throws<ArgumentException>(
            () => new RampGenerator { Min = 100, Max = 0, Step = 10 }.ValueAt(0));

    [Fact]
    public void NegativeTickIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToggleGenerator().ValueAt(-1));

    private static int[] Ticks(IValueGenerator generator, int count)
        => Enumerable.Range(0, count).Select(generator.ValueAt).ToArray();
}
