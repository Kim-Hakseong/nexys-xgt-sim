namespace Nxs.Core.Memory;

/// <summary>
/// <c>%MW0.10</c> 처럼 "워드 안의 몇 번째 비트" 표기를 절대 비트 주소로 바꾼다.
/// </summary>
/// <remarks>
/// <para>
/// 사용자는 "MW0 의 10번째 비트" 로 생각하는데, 그것을 IEC 표기로 쓰려면 <c>%MX10</c> 처럼
/// **절대 비트 번호**를 직접 계산해야 한다. MW1 의 10번째 비트가 <c>%MX26</c> 이라는 계산은
/// 사람이 매번 할 일이 아니다 — 틀리기도 쉽고, 틀려도 티가 안 난다.
/// </para>
/// <para>
/// 그래서 <c>%MW1.10</c> 을 받아 <c>%MX26</c> 으로 바꿔 준다. 일반 주소는 그대로 통과시킨다.
/// </para>
/// </remarks>
public static class BitNotation
{
    /// <summary>
    /// 표기를 주소로 바꾼다. <c>워드.비트</c> 형태면 절대 비트 주소로 환산한다.
    /// </summary>
    /// <exception cref="FormatException">표기를 해석할 수 없을 때.</exception>
    /// <exception cref="ArgumentOutOfRangeException">비트 번호가 그 주소의 폭을 벗어날 때.</exception>
    public static IecAddress Parse(string? text, AddressingOptions? addressing = null)
    {
        if (!TryParse(text, out var address, out var error, addressing))
        {
            throw error is ArgumentOutOfRangeException
                ? error
                : new FormatException(error?.Message ?? $"주소를 해석할 수 없습니다: {text}");
        }

        return address!;
    }

    /// <summary>표기를 주소로 바꾼다. 실패하면 false 와 사유를 돌려준다.</summary>
    public static bool TryParse(
        string? text,
        out IecAddress? address,
        out Exception? error,
        AddressingOptions? addressing = null)
    {
        address = null;
        error = null;

        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            error = new FormatException("주소가 비어 있습니다");
            return false;
        }

        var dot = raw.LastIndexOf('.');
        if (dot < 0)
        {
            if (!IecAddress.TryParse(raw, addressing ?? AddressingOptions.Default, out var plain))
            {
                error = new FormatException($"주소를 해석할 수 없습니다: {raw}");
                return false;
            }

            address = plain;
            return true;
        }

        var wordText = raw[..dot];
        var bitText = raw[(dot + 1)..];

        if (!int.TryParse(bitText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var bitIndex))
        {
            error = new FormatException($"비트 번호를 해석할 수 없습니다: '{bitText}'");
            return false;
        }

        if (!IecAddress.TryParse(wordText, addressing ?? AddressingOptions.Default, out var word))
        {
            error = new FormatException($"주소를 해석할 수 없습니다: {wordText}");
            return false;
        }

        if (word.Size == DataSize.Bit)
        {
            error = new FormatException(
                $"{word.Text} 는 이미 비트 주소입니다 — '.{bitText}' 를 붙일 수 없습니다");
            return false;
        }

        var bitCount = word.ByteLength * 8;
        if (bitIndex < 0 || bitIndex >= bitCount)
        {
            error = new ArgumentOutOfRangeException(
                nameof(text), bitIndex,
                $"{word.Text} 는 비트 0..{bitCount - 1} 만 있습니다");
            return false;
        }

        var absolute = (word.ByteStart * 8) + bitIndex;
        address = IecAddress.Parse(
            $"%{AreaChar(word.Area)}X{absolute.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            addressing ?? AddressingOptions.Default);
        return true;
    }

    /// <summary>표기가 <c>워드.비트</c> 형태인지.</summary>
    public static bool LooksLikeBitOfWord(string? text)
        => text is not null && text.Contains('.', StringComparison.Ordinal);

    private static char AreaChar(MemoryArea area) => area switch
    {
        MemoryArea.I => 'I',
        MemoryArea.Q => 'Q',
        MemoryArea.M => 'M',
        _ => throw new InvalidOperationException($"알 수 없는 영역: {area}"),
    };
}
