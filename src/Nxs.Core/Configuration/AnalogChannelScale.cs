namespace Nxs.Core.Configuration;

/// <summary>
/// AD 채널 스케일 — 공학단위 ↔ raw 변환 (PRD X-05).
/// DESIGN: 자동화 룰(공학단위 min/max)이 이 스케일을 공유한다.
/// </summary>
public sealed record AnalogChannelScale
{
    /// <summary>raw 최소값.</summary>
    public int RawMin { get; init; }

    /// <summary>raw 최대값.</summary>
    public int RawMax { get; init; } = 65535;

    /// <summary>공학단위 최소값.</summary>
    public double EngineeringMin { get; init; }

    /// <summary>공학단위 최대값.</summary>
    public double EngineeringMax { get; init; } = 65535;

    /// <summary>공학단위 표기. 예: <c>V</c>, <c>mA</c>, <c>℃</c>.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>변환 없는 raw 통과 스케일.</summary>
    public static AnalogChannelScale Default { get; } = new();

    /// <summary>공학단위 값을 raw 로 변환한다. 범위를 벗어나면 raw 경계로 클램프한다.</summary>
    /// <exception cref="ArgumentException">공학단위 범위 폭이 0일 때.</exception>
    public int ToRaw(double engineeringValue)
    {
        var span = EngineeringMax - EngineeringMin;
        if (span == 0)
        {
            throw new ArgumentException(
                $"공학단위 범위 폭이 0입니다 (min={EngineeringMin}, max={EngineeringMax})", nameof(engineeringValue));
        }

        var ratio = (engineeringValue - EngineeringMin) / span;
        var raw = RawMin + (ratio * (RawMax - RawMin));
        var rounded = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, Math.Min(RawMin, RawMax), Math.Max(RawMin, RawMax));
    }

    /// <summary>raw 값을 공학단위로 변환한다.</summary>
    /// <exception cref="ArgumentException">raw 범위 폭이 0일 때.</exception>
    public double ToEngineering(int raw)
    {
        var span = RawMax - RawMin;
        if (span == 0)
        {
            throw new ArgumentException($"raw 범위 폭이 0입니다 (min={RawMin}, max={RawMax})", nameof(raw));
        }

        var ratio = (double)(raw - RawMin) / span;
        return EngineeringMin + (ratio * (EngineeringMax - EngineeringMin));
    }

    /// <summary>
    /// 실수 raw 값을 공학단위로 변환한다. raw 를 Float/Double 형식으로 다루는 채널용이다.
    /// </summary>
    /// <remarks>
    /// <see cref="ToEngineering(int)"/> 는 정수 raw 를 전제한다. 마스터가 워드에 IEEE754 실수를
    /// 넣는 경우 raw 자체가 실수이므로 정수로 깎으면 소수부가 사라진다.
    /// </remarks>
    /// <exception cref="ArgumentException">raw 범위 폭이 0일 때.</exception>
    public double ToEngineering(double raw)
    {
        var span = RawMax - RawMin;
        if (span == 0)
        {
            throw new ArgumentException($"raw 범위 폭이 0입니다 (min={RawMin}, max={RawMax})", nameof(raw));
        }

        return EngineeringMin + ((raw - RawMin) / span * (EngineeringMax - EngineeringMin));
    }

    /// <summary>
    /// 공학단위 값을 **실수 raw** 로 변환한다. 정수로 반올림하지 않고 raw 경계로만 클램프한다.
    /// </summary>
    /// <exception cref="ArgumentException">공학단위 범위 폭이 0일 때.</exception>
    public double ToRawValue(double engineeringValue)
    {
        var span = EngineeringMax - EngineeringMin;
        if (span == 0)
        {
            throw new ArgumentException(
                $"공학단위 범위 폭이 0입니다 (min={EngineeringMin}, max={EngineeringMax})",
                nameof(engineeringValue));
        }

        var raw = RawMin + ((engineeringValue - EngineeringMin) / span * (RawMax - RawMin));
        return Math.Clamp(raw, Math.Min(RawMin, RawMax), Math.Max(RawMin, RawMax));
    }

    /// <summary>raw 값을 메모리에 담을 워드로 바꾼다(2의 보수).</summary>
    public static ushort RawToWord(int raw) => unchecked((ushort)raw);

    /// <summary>메모리 워드를 부호 있는 raw 값으로 되돌린다.</summary>
    public static int WordToRaw(ushort word) => unchecked((short)word);
}
