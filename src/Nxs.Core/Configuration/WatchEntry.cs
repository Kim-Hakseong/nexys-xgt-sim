using System.Globalization;
using System.Text;
using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>워치 값 표시 형식.</summary>
public enum WatchFormat
{
    /// <summary>부호 없는 10진.</summary>
    Decimal,

    /// <summary>부호 있는 10진 (워드는 int16, 더블워드는 int32).</summary>
    Signed,

    /// <summary>16진 (크기에 맞춰 자릿수 고정).</summary>
    Hex,

    /// <summary>2진 (4비트씩 띄어쓰기).</summary>
    Binary,

    /// <summary>ON/OFF.</summary>
    Bool,
}

/// <summary>
/// 사용자 지정 워치 항목 — 랙 매핑과 무관한 임의 주소를 직접 보고 쓴다.
/// </summary>
/// <remarks>
/// LabVIEW 는 대부분 <c>%M</c> 영역과 대화하는데 그 주소는 I/O 랙 매핑에 나타나지 않는다.
/// 이 목록이 있어야 <c>%MW320</c>, <c>%MD422</c> 같은 실제 교신 주소를 눈으로 확인하며 티키타카할 수 있다.
/// </remarks>
public sealed record WatchEntry
{
    /// <summary>IEC 주소 표기. 예: <c>%MW320</c>, <c>%MD422</c>, <c>%MX801</c>.</summary>
    public required string Address { get; init; }

    /// <summary>사용자 별칭(무엇을 뜻하는 주소인지).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>표시 형식.</summary>
    public WatchFormat Format { get; init; } = WatchFormat.Decimal;

    /// <summary>주소를 해석한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    public IecAddress Resolve(AddressingOptions? addressing = null)
        => IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);

    /// <summary>주소 표기가 유효한지 (목록에 넣기 전 검사).</summary>
    public static bool IsValid(string? address)
        => !string.IsNullOrWhiteSpace(address) && IecAddress.TryParse(address, out _);

    /// <summary>값을 지정 형식으로 표기한다.</summary>
    public static string Render(uint value, DataSize size, WatchFormat format) => format switch
    {
        WatchFormat.Decimal => value.ToString(CultureInfo.InvariantCulture),
        WatchFormat.Signed => RenderSigned(value, size),
        WatchFormat.Hex => "0x" + value.ToString("X" + (size.BitWidth() + 3) / 4, CultureInfo.InvariantCulture),
        WatchFormat.Binary => RenderBinary(value, size),
        WatchFormat.Bool => value != 0 ? "ON" : "OFF",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// 사용자 입력을 값으로 해석한다. 10진 / <c>0x</c> 16진 / ON·OFF·true·false 를 받는다.
    /// </summary>
    /// <returns>해석된 값, 해석 실패나 범위 초과면 null.</returns>
    public static uint? ParseInput(string? text, DataSize size)
    {
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (size == DataSize.Bit)
        {
            if (raw.Equals("ON", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return 1u;
            }

            if (raw.Equals("OFF", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return 0u;
            }
        }

        var max = size.BitWidth() >= 32 ? uint.MaxValue : (1u << size.BitWidth()) - 1;

        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                && hex <= max
                    ? hex
                    : null;
        }

        if (raw.StartsWith('-'))
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
            {
                return null;
            }

            var min = -(long)((max >> 1) + 1);
            return signed >= min && signed < 0 ? (uint)(signed & max) : null;
        }

        return ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value <= max
                ? (uint)value
                : null;
    }

    private static string RenderSigned(uint value, DataSize size) => size switch
    {
        DataSize.Word => ((short)value).ToString(CultureInfo.InvariantCulture),
        DataSize.DWord => ((int)value).ToString(CultureInfo.InvariantCulture),
        DataSize.Byte => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string RenderBinary(uint value, DataSize size)
    {
        var width = size.BitWidth();
        var sb = new StringBuilder(width + (width / 4));
        for (var bit = width - 1; bit >= 0; bit--)
        {
            sb.Append((value >> bit) & 1);
            if (bit % 4 == 0 && bit != 0)
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }
}
