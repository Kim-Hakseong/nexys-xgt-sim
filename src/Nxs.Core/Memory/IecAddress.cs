using System.Globalization;

namespace Nxs.Core.Memory;

/// <summary>
/// IEC 직접변수 주소. 불변.
/// 표기: <c>%&lt;area&gt;&lt;size&gt;&lt;offset&gt;</c> (예: %MW100, %MX801) 또는
/// 슬롯 형식 <c>%&lt;area&gt;&lt;size&gt;&lt;base&gt;.&lt;slot&gt;.&lt;point&gt;</c> (예: %IX0.2.5).
/// </summary>
public sealed record IecAddress
{
    /// <summary>대상 메모리 영역.</summary>
    public required MemoryArea Area { get; init; }

    /// <summary>크기 지정자.</summary>
    public required DataSize Size { get; init; }

    /// <summary>크기 지정자 단위의 절대 오프셋.</summary>
    public required int Offset { get; init; }

    /// <summary>원문 표기.</summary>
    public required string Text { get; init; }

    /// <summary>참조 범위 시작 바이트(포함).</summary>
    public int ByteStart => Size == DataSize.Bit
        ? Offset / 8
        : Offset * (Size.BitWidth() / 8);

    /// <summary>참조 범위 끝 바이트(제외). 로그 하이라이트용 [ByteStart, ByteEnd) 범위.</summary>
    public int ByteEnd => Size == DataSize.Bit
        ? ByteStart + 1
        : ByteStart + (Size.BitWidth() / 8);

    /// <summary>참조 범위 바이트 길이.</summary>
    public int ByteLength => ByteEnd - ByteStart;

    /// <summary>비트 주소일 때 <see cref="ByteStart"/> 내 비트 위치(0..7).</summary>
    /// <exception cref="InvalidOperationException">비트 주소가 아닐 때.</exception>
    public int BitInByte => Size == DataSize.Bit
        ? Offset % 8
        : throw new InvalidOperationException($"비트 주소가 아닙니다: {Text}");

    /// <summary>IEC 주소를 파싱한다(기본 주소 산법 설정).</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    public static IecAddress Parse(string text) => Parse(text, AddressingOptions.Default);

    /// <summary>IEC 주소를 파싱한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    public static IecAddress Parse(string text, AddressingOptions options)
        => TryParse(text, options, out var address)
            ? address
            : throw new FormatException($"올바른 IEC 주소가 아닙니다: '{text}'");

    /// <summary>IEC 주소 파싱을 시도한다(기본 주소 산법 설정).</summary>
    public static bool TryParse(string? text, out IecAddress address)
        => TryParse(text, AddressingOptions.Default, out address);

    /// <summary>IEC 주소 파싱을 시도한다.</summary>
    public static bool TryParse(string? text, AddressingOptions options, out IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(options);
        address = null!;

        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length < 4 || raw[0] != '%')
        {
            return false;
        }

        if (!TryParseArea(char.ToUpperInvariant(raw[1]), out var area))
        {
            return false;
        }

        if (!TryParseSize(char.ToUpperInvariant(raw[2]), out var size))
        {
            return false;
        }

        options.Validate();

        var digits = raw[3..];
        var parts = digits.Split('.');

        int offset;
        switch (parts.Length)
        {
            case 1:
                if (!TryParseIndex(parts[0], out offset))
                {
                    return false;
                }

                break;

            case 3:
                if (!TryParseIndex(parts[0], out var baseNo) ||
                    !TryParseIndex(parts[1], out var slotNo) ||
                    !TryParseIndex(parts[2], out var point))
                {
                    return false;
                }

                if (slotNo >= options.SlotsPerBase)
                {
                    return false;
                }

                // 슬롯 시작 비트를 크기 지정자 단위로 환산한 뒤 점 번호를 더한다 (DESIGN 산식).
                var slotStartBit = (long)baseNo * options.BasePoints + (long)slotNo * options.SlotPoints;
                var units = slotStartBit / size.BitWidth() + point;
                if (units > int.MaxValue)
                {
                    return false;
                }

                offset = (int)units;
                break;

            default:
                return false;
        }

        address = new IecAddress { Area = area, Size = size, Offset = offset, Text = raw };
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private static bool TryParseArea(char c, out MemoryArea area)
    {
        switch (c)
        {
            case 'I': area = MemoryArea.I; return true;
            case 'Q': area = MemoryArea.Q; return true;
            case 'M': area = MemoryArea.M; return true;
            default: area = default; return false;
        }
    }

    private static bool TryParseSize(char c, out DataSize size)
    {
        switch (c)
        {
            case 'X': size = DataSize.Bit; return true;
            case 'B': size = DataSize.Byte; return true;
            case 'W': size = DataSize.Word; return true;
            case 'D': size = DataSize.DWord; return true;
            default: size = default; return false;
        }
    }

    private static bool TryParseIndex(string s, out int value)
    {
        value = 0;
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
