using System.Text;

namespace Nxs.Core.Memory;

/// <summary>
/// 사용자가 손으로 입력한 주소 문자열을 파서가 받을 수 있는 형태로 정규화한다.
/// </summary>
/// <remarks>
/// <para>
/// UI 입력은 깨끗하지 않다. 특히 한글 IME 가 켜진 상태에서는 <c>%</c> 대신 전각 <c>％</c>(U+FF05),
/// <c>M</c> 대신 <c>Ｍ</c>(U+FF2D), <c>0</c> 대신 <c>０</c>(U+FF10) 이 들어간다.
/// 이 문자들은 화면에서 거의 같아 보이지만 파서는 거부하므로 "왜 안 되는지 모르겠는" 증상이 된다.
/// </para>
/// <para>
/// 처리하는 것: 앞뒤·내부 공백 제거 · 전각 영숫자/기호를 ASCII 로 변환 · 소문자를 대문자로 ·
/// 빠뜨린 선행 <c>%</c> 보충.
/// </para>
/// </remarks>
public static class AddressInput
{
    /// <summary>전각 문자 영역 시작(<c>！</c>).</summary>
    private const char FullWidthStart = '！';

    /// <summary>전각 문자 영역 끝(<c>～</c>).</summary>
    private const char FullWidthEnd = '～';

    /// <summary>전각 → ASCII 오프셋.</summary>
    private const int FullWidthOffset = 0xFF01 - '!';

    /// <summary>
    /// 주소 문자열을 정규화한다. 빈 입력은 빈 문자열을 반환한다.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length + 1);

        foreach (var raw in text)
        {
            var c = raw;

            // 전각 영숫자·기호를 ASCII 로 접는다 (한글 IME 가 만드는 가장 흔한 실패 원인).
            if (c is >= FullWidthStart and <= FullWidthEnd)
            {
                c = (char)(c - FullWidthOffset);
            }
            else if (c == '　')
            {
                // 전각 공백
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(c));
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        // 선행 % 를 빠뜨렸으면 보충한다 (MW320 → %MW320).
        if (sb[0] != '%')
        {
            sb.Insert(0, '%');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 정규화 후에도 파싱할 수 없을 때 사람이 원인을 알 수 있는 설명을 만든다.
    /// </summary>
    /// <remarks>
    /// 눈에 보이지 않는 문자가 원인인 경우가 있으므로 코드포인트를 함께 보여준다.
    /// </remarks>
    public static string Describe(string? original)
    {
        var raw = original ?? string.Empty;
        if (raw.Length == 0)
        {
            return "빈 입력";
        }

        var codes = string.Join(" ", raw.Select(c => $"U+{(int)c:X4}"));
        return $"'{raw}' (문자 코드: {codes})";
    }
}
