namespace Nxs.Core.Automation;

/// <summary>제너레이터 공통 검증 헬퍼.</summary>
internal static class GeneratorGuards
{
    internal static void RequireNonNegativeTick(int tickIndex)
    {
        if (tickIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickIndex), tickIndex, "tick 은 0 이상이어야 합니다");
        }
    }

    internal static void RequireAscendingRange(int min, int max)
    {
        if (max < min)
        {
            throw new ArgumentException($"Max({max})는 Min({min}) 이상이어야 합니다", nameof(max));
        }
    }

    internal static void RequirePositiveStep(int step)
    {
        if (step <= 0)
        {
            throw new ArgumentException($"Step 은 1 이상이어야 합니다. 실제: {step}", nameof(step));
        }
    }
}

/// <summary>고정값.</summary>
public sealed record FixedGenerator : IValueGenerator
{
    /// <summary>항상 반환할 값.</summary>
    public int Value { get; init; }

    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Fixed;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        return Value;
    }
}

/// <summary>모듈러 카운터 — Min 부터 Step 씩 증가하며 범위를 감싼다.</summary>
public sealed record IncrementGenerator : IValueGenerator
{
    /// <summary>최소값.</summary>
    public int Min { get; init; }

    /// <summary>최대값(포함).</summary>
    public int Max { get; init; } = 65535;

    /// <summary>tick 당 증가량.</summary>
    public int Step { get; init; } = 1;

    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Increment;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        GeneratorGuards.RequireAscendingRange(Min, Max);
        GeneratorGuards.RequirePositiveStep(Step);

        var span = (long)Max - Min + 1;
        return (int)(Min + ((long)tickIndex * Step % span));
    }
}

/// <summary>
/// 램프 — Min 에서 Step 씩 올라 Max 를 정확히 찍은 다음 Min 으로 복귀한다.
/// </summary>
/// <remarks>
/// DESIGN 골든 벡터: Ramp(0,100,25) → 0,25,50,75,100,0.
/// 즉 한 주기의 값 개수는 (Max-Min)/Step + 1 이고, 그 다음 tick 에서 Min 으로 돌아온다.
/// </remarks>
public sealed record RampGenerator : IValueGenerator
{
    /// <summary>최소값.</summary>
    public int Min { get; init; }

    /// <summary>최대값(정확히 도달한다).</summary>
    public int Max { get; init; } = 65535;

    /// <summary>tick 당 증가량.</summary>
    public int Step { get; init; } = 1;

    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Ramp;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        GeneratorGuards.RequireAscendingRange(Min, Max);
        GeneratorGuards.RequirePositiveStep(Step);

        var steps = ((long)Max - Min) / Step + 1;
        return (int)(Min + (tickIndex % steps * Step));
    }
}

/// <summary>사인파 — Period tick 마다 한 주기.</summary>
/// <remarks>
/// DESIGN 골든 벡터: Sine(0,1000,period4) → 500,1000,500,0.
/// 중앙값 (Min+Max)/2 에서 시작해 진폭 (Max-Min)/2 로 진동한다.
/// </remarks>
public sealed record SineGenerator : IValueGenerator
{
    /// <summary>최소값.</summary>
    public int Min { get; init; }

    /// <summary>최대값.</summary>
    public int Max { get; init; } = 65535;

    /// <summary>한 주기의 tick 수.</summary>
    public int Period { get; init; } = 60;

    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Sine;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        GeneratorGuards.RequireAscendingRange(Min, Max);
        if (Period <= 0)
        {
            throw new ArgumentException($"Period 는 1 이상이어야 합니다. 실제: {Period}", nameof(Period));
        }

        var mid = (Min + Max) / 2.0;
        var amplitude = (Max - Min) / 2.0;
        var phase = 2.0 * Math.PI * (tickIndex % Period) / Period;
        var value = mid + (amplitude * Math.Sin(phase));
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 랜덤 — 시드와 tick 의 해시로 값을 만든다. 상태가 없어 **재현 가능**하다.
/// </summary>
/// <remarks>
/// System.Random 인스턴스를 쓰면 호출 순서에 값이 의존해 순수 함수 계약이 깨진다.
/// 그래서 (seed, tick) → 값 해시(splitmix64 계열)를 쓴다.
/// </remarks>
public sealed record RandomGenerator : IValueGenerator
{
    /// <summary>최소값.</summary>
    public int Min { get; init; }

    /// <summary>최대값(포함).</summary>
    public int Max { get; init; } = 65535;

    /// <summary>시드.</summary>
    public int Seed { get; init; }

    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Random;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        GeneratorGuards.RequireAscendingRange(Min, Max);

        var span = (ulong)((long)Max - Min + 1);
        return (int)(Min + (long)(Hash((ulong)(uint)Seed * 0x9E3779B97F4A7C15UL + (ulong)(uint)tickIndex) % span));
    }

    private static ulong Hash(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}

/// <summary>토글 — 짝수 tick 에서 1, 홀수 tick 에서 0.</summary>
/// <remarks>DESIGN 골든 벡터: Toggle → T,F,T.</remarks>
public sealed record ToggleGenerator : IValueGenerator
{
    /// <inheritdoc />
    public GeneratorKind Kind => GeneratorKind.Toggle;

    /// <inheritdoc />
    public int ValueAt(int tickIndex)
    {
        GeneratorGuards.RequireNonNegativeTick(tickIndex);
        return tickIndex % 2 == 0 ? 1 : 0;
    }
}
