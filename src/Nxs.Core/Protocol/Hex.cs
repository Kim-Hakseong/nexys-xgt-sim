using System.Text;

namespace Nxs.Core.Protocol;

/// <summary>raw hex 표기 헬퍼 (트래픽 로그 · 캡처 픽스처용).</summary>
public static class Hex
{
    /// <summary>바이트를 대문자 hex + 공백 구분으로 표기한다. 예: <c>"00 0F A5 FF"</c>.</summary>
    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(data.Length * 3 - 1);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>hex 표기를 바이트로 되돌린다. 모든 공백류 문자는 무시한다.</summary>
    /// <exception cref="FormatException">hex 문자가 아니거나 자릿수가 홀수일 때.</exception>
    public static byte[] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var digits = new List<byte>(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            digits.Add(ParseNibble(c));
        }

        if (digits.Count % 2 != 0)
        {
            throw new FormatException($"hex 자릿수가 홀수입니다({digits.Count}): '{text}'");
        }

        var result = new byte[digits.Count / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (byte)((digits[i * 2] << 4) | digits[(i * 2) + 1]);
        }

        return result;
    }

    private static byte ParseNibble(char c) => c switch
    {
        >= '0' and <= '9' => (byte)(c - '0'),
        >= 'a' and <= 'f' => (byte)(c - 'a' + 10),
        >= 'A' and <= 'F' => (byte)(c - 'A' + 10),
        _ => throw new FormatException($"hex 문자가 아닙니다: '{c}'"),
    };
}
