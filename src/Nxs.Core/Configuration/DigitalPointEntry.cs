using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>사용자 지정 디지털 점의 동작 방향.</summary>
public enum DigitalPointMode
{
    /// <summary>
    /// 입력 — 사용자가 토글하면 메모리에 쓴다. 마스터가 읽어 확인한다.
    /// 외부에서 값이 바뀌면 표시도 따라간다(양방향 확인).
    /// </summary>
    Input,

    /// <summary>출력 — 마스터가 쓴 값을 LED 로 표시만 한다. 사용자 조작 불가.</summary>
    Output,
}

/// <summary>
/// 사용자 지정 디지털 점 — 랙 매핑 밖의 임의 비트 주소를 토글하거나 감시한다.
/// </summary>
/// <remarks>
/// 랙 매핑은 고정 주소(<c>%IX512</c> 등)만 보여주므로, 마스터가 실제로 쓰는
/// <c>%MX801</c>·<c>%QX2000</c> 같은 비트를 확인할 방법이 없었다.
/// 입력 모드로 넣으면 토글 → 마스터 읽기, 출력 모드로 넣으면 마스터 쓰기 → LED 확인이 되어
/// **불리언 ON/OFF 를 양방향으로 검증**할 수 있다.
/// </remarks>
public sealed record DigitalPointEntry
{
    /// <summary>비트 주소 표기. 예: <c>%MX801</c>, <c>%IX600</c>, <c>%QX2000</c>.</summary>
    public required string Address { get; init; }

    /// <summary>사용자 별칭.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>동작 방향.</summary>
    public DigitalPointMode Mode { get; init; } = DigitalPointMode.Input;

    /// <summary>주소를 해석한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    /// <exception cref="InvalidOperationException">비트 주소가 아닐 때.</exception>
    public IecAddress Resolve(AddressingOptions? addressing = null)
    {
        var address = IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);
        if (address.Size != DataSize.Bit)
        {
            throw new InvalidOperationException(
                $"디지털 점은 비트 주소여야 합니다(%..X). 실제: {address.Text}");
        }

        return address;
    }

    /// <summary>비트 주소로 유효한지 (목록에 넣기 전 검사).</summary>
    public static bool IsValid(string? address)
        => !string.IsNullOrWhiteSpace(address)
            && IecAddress.TryParse(address, out var parsed)
            && parsed.Size == DataSize.Bit;
}
