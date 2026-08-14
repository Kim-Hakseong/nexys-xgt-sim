using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nxs.Core.Configuration;

/// <summary>
/// 바이트 순서(워드오더). 값 해석 기준을 마스터(LabVIEW)와 맞추기 위한 것이다.
/// </summary>
/// <remarks>
/// 이름의 A·B·C·D 는 **메모리에 놓인 순서**(오프셋 낮은 쪽부터)를 가리킨다.
/// 표기 관례는 자매 프로젝트 nexys-modbus-workbench 와 동일하다.
/// </remarks>
public enum ByteOrder
{
    /// <summary>빅엔디안 — 첫 메모리 바이트가 최상위. (A B C D)</summary>
    Abcd,

    /// <summary>리틀엔디안 — 마지막 메모리 바이트가 최상위. XGT 저장 방식. (D C B A)</summary>
    Dcba,

    /// <summary>워드 안에서 바이트만 스왑. (B A D C)</summary>
    Badc,

    /// <summary>워드 순서만 스왑. (C D A B)</summary>
    Cdab,
}

/// <summary>바이트 순서 표시 이름.</summary>
public static class ByteOrderExtensions
{
    /// <summary>UI 표시 이름.</summary>
    public static string Label(this ByteOrder order) => order switch
    {
        ByteOrder.Abcd => "ABCD (빅엔디안)",
        ByteOrder.Dcba => "DCBA (리틀엔디안)",
        ByteOrder.Badc => "BADC (바이트 스왑)",
        ByteOrder.Cdab => "CDAB (워드 스왑)",
        _ => order.ToString(),
    };
}

/// <summary>
/// 메모리 바이트 ↔ 표시 문자열 변환.
/// </summary>
/// <remarks>
/// <para>
/// 값을 <c>uint</c> 로 먼저 바꾸지 않고 **바이트를 직접 다룬다.** 엔디안은 본질적으로
/// 바이트 순서 문제이고, Double(8바이트)은 32비트 정수에 담기지 않기 때문이다.
/// </para>
/// <para>
/// 내부 표현은 "MSB 우선(빅엔디안) 바이트열"로 통일한다 — <see cref="ToMsbFirst"/> 로 들어오고
/// <see cref="FromMsbFirst"/> 로 나간다. 모든 순서 변환은 자기 역변환이다(대합적 치환).
/// </para>
/// </remarks>
public static class WatchValue
{
    /// <summary>Float(Single)에 필요한 바이트 수.</summary>
    public const int FloatBytes = 4;

    /// <summary>Double 에 필요한 바이트 수.</summary>
    public const int DoubleBytes = 8;

    /// <summary>메모리 바이트를 MSB 우선 순서로 재배열한다.</summary>
    public static byte[] ToMsbFirst(ReadOnlySpan<byte> memoryBytes, ByteOrder order)
        => Permute(memoryBytes, order);

    /// <summary>MSB 우선 바이트를 메모리 배치 순서로 되돌린다.</summary>
    public static byte[] FromMsbFirst(ReadOnlySpan<byte> msbFirst, ByteOrder order)
        => Permute(msbFirst, order);

    /// <summary>메모리 바이트를 지정 형식으로 표기한다.</summary>
    /// <param name="memoryBytes">메모리에 놓인 순서의 바이트.</param>
    /// <param name="format">표시 형식.</param>
    /// <param name="order">바이트 순서.</param>
    /// <returns>표시 문자열. 형식이 폭과 맞지 않으면 그 사실을 알리는 문구.</returns>
    public static string Render(ReadOnlySpan<byte> memoryBytes, WatchFormat format, ByteOrder order)
    {
        if (memoryBytes.IsEmpty)
        {
            return string.Empty;
        }

        var msb = ToMsbFirst(memoryBytes, order);

        switch (format)
        {
            case WatchFormat.Float:
                return msb.Length == FloatBytes
                    ? Invariant(BinaryPrimitives.ReadSingleBigEndian(msb))
                    : $"— Float은 {FloatBytes}바이트(%..D) 주소가 필요합니다";

            case WatchFormat.Double:
                return msb.Length == DoubleBytes
                    ? Invariant(BinaryPrimitives.ReadDoubleBigEndian(msb))
                    : $"— Double은 {DoubleBytes}바이트(%..L) 주소가 필요합니다";

            case WatchFormat.Bool:
                return msb.Any(b => b != 0) ? "ON" : "OFF";

            case WatchFormat.Hex:
                return "0x" + Convert.ToHexString(msb);

            case WatchFormat.Binary:
                return RenderBinary(msb);

            case WatchFormat.Signed:
                return RenderSigned(msb);

            case WatchFormat.Decimal:
            default:
                return RenderUnsigned(msb);
        }
    }

    /// <summary>
    /// 사용자 입력을 메모리 바이트로 변환한다.
    /// </summary>
    /// <param name="text">입력 문자열. 10진 / <c>0x</c>16진 / 음수 / 소수 / ON·OFF.</param>
    /// <param name="byteLength">대상 주소의 바이트 폭.</param>
    /// <param name="format">표시 형식(파싱 규칙을 정한다).</param>
    /// <param name="order">바이트 순서.</param>
    /// <returns>메모리에 쓸 바이트, 해석 실패면 null.</returns>
    public static byte[]? Parse(string? text, int byteLength, WatchFormat format, ByteOrder order)
    {
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw) || byteLength <= 0)
        {
            return null;
        }

        switch (format)
        {
            case WatchFormat.Float:
            {
                if (byteLength != FloatBytes
                    || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var single)
                    || !float.IsFinite(single))
                {
                    return null;
                }

                var buffer = new byte[FloatBytes];
                BinaryPrimitives.WriteSingleBigEndian(buffer, single);
                return FromMsbFirst(buffer, order);
            }

            case WatchFormat.Double:
            {
                if (byteLength != DoubleBytes
                    || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !double.IsFinite(value))
                {
                    return null;
                }

                var buffer = new byte[DoubleBytes];
                BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
                return FromMsbFirst(buffer, order);
            }

            case WatchFormat.Bool:
            {
                if (raw.Equals("ON", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return Integer(1, byteLength, order);
                }

                if (raw.Equals("OFF", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return Integer(0, byteLength, order);
                }

                break;
            }
        }

        // 정수 계열 (Decimal / Signed / Hex / Binary, 그리고 Bool 의 숫자 입력)
        if (byteLength > 8)
        {
            return null;
        }

        var bits = byteLength * 8;
        var max = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;

        // 2진 형식은 화면에 "0001 0010 0011 0100" 처럼 나오므로 그 표기를 그대로 되받는다.
        // 되받지 못하면 사용자가 화면의 값을 고쳐 쓸 수 없어 형식 선택이 반쪽이 된다.
        if (format is WatchFormat.Binary)
        {
            var digits = raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
            digits = digits.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);

            if (digits.Length == 0 || digits.Length > bits || digits.Any(c => c is not ('0' or '1')))
            {
                return null;
            }

            ulong binary = 0;
            foreach (var c in digits)
            {
                binary = (binary << 1) | (uint)(c - '0');
            }

            return Integer(binary, byteLength, order);
        }

        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                && hex <= max
                    ? Integer(hex, byteLength, order)
                    : null;
        }

        if (raw.StartsWith('-'))
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
            {
                return null;
            }

            var min = bits >= 64 ? long.MinValue : -(long)((max >> 1) + 1);
            return signed >= min && signed < 0
                ? Integer(unchecked((ulong)signed) & max, byteLength, order)
                : null;
        }

        return ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned)
            && unsigned <= max
                ? Integer(unsigned, byteLength, order)
                : null;
    }

    /// <summary>바이트를 부호 있는 정수로 해석한다(아날로그 raw 값용).</summary>
    public static long ToSigned(ReadOnlySpan<byte> memoryBytes, ByteOrder order)
    {
        if (memoryBytes.IsEmpty)
        {
            return 0;
        }

        var msb = ToMsbFirst(memoryBytes, order);
        ulong raw = 0;
        foreach (var b in msb)
        {
            raw = (raw << 8) | b;
        }

        var bits = msb.Length * 8;
        if (bits >= 64)
        {
            return unchecked((long)raw);
        }

        var signBit = 1UL << (bits - 1);
        return (raw & signBit) != 0
            ? unchecked((long)raw) - (long)(1UL << bits)
            : (long)raw;
    }

    /// <summary>부호 있는 정수를 지정 폭·순서의 바이트로 만든다.</summary>
    public static byte[] FromSigned(long value, int byteLength, ByteOrder order)
    {
        var mask = byteLength >= 8 ? ulong.MaxValue : (1UL << (byteLength * 8)) - 1;
        return Integer(unchecked((ulong)value) & mask, byteLength, order);
    }

    /// <summary>지정 폭에서 표현 가능한 부호 있는 정수 범위.</summary>
    public static (long Min, long Max) SignedRange(int byteLength)
    {
        if (byteLength >= 8)
        {
            return (long.MinValue, long.MaxValue);
        }

        var half = 1L << ((byteLength * 8) - 1);
        return (-half, half - 1);
    }

    /// <summary>
    /// 바이트를 지정 형식의 **수치**로 해석한다. A/D 채널이 raw ↔ 공학단위를 환산할 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <see cref="Render"/> 는 사람이 읽는 문자열을 만들지만, 스케일 계산에는 수치가 필요하다.
    /// 두 경로가 같은 형식·순서 해석을 쓰도록 여기 한곳에 모아 둔다 —
    /// 표시와 계산이 갈라지면 화면 값과 메모리 값이 어긋난다.
    /// </remarks>
    /// <returns>수치. 형식이 폭과 맞지 않으면 null.</returns>
    public static double? ToNumber(ReadOnlySpan<byte> memoryBytes, WatchFormat format, ByteOrder order)
    {
        if (memoryBytes.IsEmpty || !SupportsWidth(format, memoryBytes.Length))
        {
            return null;
        }

        var msb = ToMsbFirst(memoryBytes, order);
        switch (format)
        {
            case WatchFormat.Float:
                return BinaryPrimitives.ReadSingleBigEndian(msb);

            case WatchFormat.Double:
                return BinaryPrimitives.ReadDoubleBigEndian(msb);

            case WatchFormat.Bool:
                return msb.Any(b => b != 0) ? 1 : 0;

            case WatchFormat.Signed:
                return ToSigned(memoryBytes, order);

            default:
            {
                // Decimal / Hex / Binary 는 모두 같은 부호 없는 정수를 다르게 표기한 것이다.
                ulong raw = 0;
                foreach (var b in msb)
                {
                    raw = (raw << 8) | b;
                }

                return raw;
            }
        }
    }

    /// <summary>
    /// 수치를 지정 형식의 메모리 바이트로 만든다. <see cref="ToNumber"/> 의 역방향이다.
    /// </summary>
    /// <remarks>
    /// 정수 계열 형식에서는 반올림한다 — 공학단위 환산은 실수를 내는데 정수 형식에 담아야 하기 때문이다.
    /// 폭에 담기지 않는 값은 null 을 돌려 조용히 잘리지 않게 한다.
    /// </remarks>
    /// <returns>메모리에 쓸 바이트. 형식·폭·범위가 맞지 않으면 null.</returns>
    public static byte[]? FromNumber(double value, int byteLength, WatchFormat format, ByteOrder order)
    {
        if (byteLength <= 0 || !SupportsWidth(format, byteLength) || !double.IsFinite(value))
        {
            return null;
        }

        switch (format)
        {
            case WatchFormat.Float:
            {
                var single = (float)value;
                if (!float.IsFinite(single))
                {
                    return null;
                }

                var buffer = new byte[FloatBytes];
                BinaryPrimitives.WriteSingleBigEndian(buffer, single);
                return FromMsbFirst(buffer, order);
            }

            case WatchFormat.Double:
            {
                var buffer = new byte[DoubleBytes];
                BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
                return FromMsbFirst(buffer, order);
            }

            case WatchFormat.Bool:
                return Integer(value != 0 ? 1UL : 0UL, byteLength, order);

            case WatchFormat.Signed:
            {
                var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
                var (min, max) = SignedRange(byteLength);
                return rounded < min || rounded > max ? null : FromSigned((long)rounded, byteLength, order);
            }

            default:
            {
                var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
                var max = byteLength >= 8 ? ulong.MaxValue : (1UL << (byteLength * 8)) - 1;
                return rounded < 0 || rounded > max ? null : Integer((ulong)rounded, byteLength, order);
            }
        }
    }

    /// <summary>
    /// 이 형식·폭이 만들어 낼 수 있는 표기의 **최대 글자 수**.
    /// </summary>
    /// <remarks>
    /// 범위 보기가 칸 너비를 정할 때 쓴다. 글꼴을 재지 않고 글자 수로 정하므로 결정적이고,
    /// 값이 바뀌어도 칸 크기가 흔들리지 않는다 — 2진 워드(<c>0000 0100 1101 0010</c>)처럼
    /// 긴 표기가 잘리던 문제를 이 값으로 막는다.
    /// </remarks>
    public static int MaxRenderedLength(WatchFormat format, int byteLength)
    {
        if (byteLength <= 0)
        {
            return 0;
        }

        return format switch
        {
            // "R" 표기는 지수부까지 나올 수 있다 — 실측 최악값에 여유를 둔다.
            WatchFormat.Float => 15,
            WatchFormat.Double => 24,
            WatchFormat.Bool => 3,
            WatchFormat.Hex => 2 + (byteLength * 2),

            // 바이트마다 8비트 + 4비트마다 공백 하나(마지막 제외).
            WatchFormat.Binary => (byteLength * 8) + (byteLength * 2) - 1,

            // 부호 있는 10진은 부호 한 자리가 더 붙는다.
            WatchFormat.Signed => DecimalDigits(byteLength) + 1,
            _ => DecimalDigits(byteLength),
        };
    }

    /// <summary>지정 폭의 부호 없는 최대값이 갖는 자릿수.</summary>
    private static int DecimalDigits(int byteLength)
    {
        if (byteLength >= 8)
        {
            return 20;   // ulong.MaxValue
        }

        var max = (1UL << (byteLength * 8)) - 1;
        return max.ToString(CultureInfo.InvariantCulture).Length;
    }

    /// <summary>이 형식이 지정 폭에서 쓸 수 있는지.</summary>
    public static bool SupportsWidth(WatchFormat format, int byteLength) => format switch
    {
        WatchFormat.Float => byteLength == FloatBytes,
        WatchFormat.Double => byteLength == DoubleBytes,
        _ => byteLength is > 0 and <= 8,
    };

    private static byte[] Integer(ulong value, int byteLength, ByteOrder order)
    {
        var msb = new byte[byteLength];
        for (var i = 0; i < byteLength; i++)
        {
            msb[byteLength - 1 - i] = (byte)(value >> (i * 8));
        }

        return FromMsbFirst(msb, order);
    }

    /// <summary>
    /// 순서 치환. 모든 경우가 자기 역변환이므로 정방향/역방향에 같은 함수를 쓴다.
    /// </summary>
    private static byte[] Permute(ReadOnlySpan<byte> source, ByteOrder order)
    {
        var result = source.ToArray();

        switch (order)
        {
            case ByteOrder.Abcd:
                break;

            case ByteOrder.Dcba:
                Array.Reverse(result);
                break;

            case ByteOrder.Badc:
                for (var i = 0; i + 1 < result.Length; i += 2)
                {
                    (result[i], result[i + 1]) = (result[i + 1], result[i]);
                }

                break;

            case ByteOrder.Cdab:
                // 워드(2바이트) 단위로 순서만 뒤집는다.
                var wordCount = result.Length / 2;
                for (var w = 0; w < wordCount / 2; w++)
                {
                    var a = w * 2;
                    var b = ((wordCount - 1 - w) * 2);
                    (result[a], result[b]) = (result[b], result[a]);
                    (result[a + 1], result[b + 1]) = (result[b + 1], result[a + 1]);
                }

                break;

            default:
                break;
        }

        return result;
    }

    private static string RenderUnsigned(ReadOnlySpan<byte> msb)
    {
        if (msb.Length > 8)
        {
            return "0x" + Convert.ToHexString(msb);
        }

        ulong value = 0;
        foreach (var b in msb)
        {
            value = (value << 8) | b;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string RenderSigned(ReadOnlySpan<byte> msb)
    {
        if (msb.Length > 8)
        {
            return "0x" + Convert.ToHexString(msb);
        }

        ulong raw = 0;
        foreach (var b in msb)
        {
            raw = (raw << 8) | b;
        }

        var bits = msb.Length * 8;
        if (bits < 64)
        {
            var signBit = 1UL << (bits - 1);
            if ((raw & signBit) != 0)
            {
                return (unchecked((long)raw) - (long)(1UL << bits)).ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            return unchecked((long)raw).ToString(CultureInfo.InvariantCulture);
        }

        return raw.ToString(CultureInfo.InvariantCulture);
    }

    private static string RenderBinary(ReadOnlySpan<byte> msb)
    {
        var sb = new StringBuilder(msb.Length * 9);
        for (var i = 0; i < msb.Length; i++)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                sb.Append((msb[i] >> bit) & 1);
                if (bit % 4 == 0 && !(i == msb.Length - 1 && bit == 0))
                {
                    sb.Append(' ');
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Invariant(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Invariant(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}
