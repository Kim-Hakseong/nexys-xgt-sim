using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>
/// 사용자 지정 A/D 채널 — 임의 주소를 아날로그 값으로 다룬다.
/// </summary>
/// <remarks>
/// <para>
/// A/D 모듈은 현장 센서의 전압·전류를 정수 raw 값으로 바꿔 PLC 워드에 넣는다.
/// 시뮬레이터는 그 반대를 한다: 사용자가 공학단위 값(예: 5 V)을 넣으면 스케일로 raw(2000)로 바꿔
/// 메모리에 써서 마스터가 실제 센서처럼 읽게 한다.
/// </para>
/// <para>
/// 랙 슬롯에 고정되지 않고 주소를 직접 지정한다 — 마스터가 실제로 읽는 주소가
/// 랙 매핑과 다를 수 있기 때문이다.
/// </para>
/// </remarks>
public sealed record AnalogPointEntry
{
    /// <summary>주소 표기. 비트를 제외한 폭(<c>%IW80</c>, <c>%MW500</c>, <c>%MD100</c>).</summary>
    public required string Address { get; init; }

    /// <summary>사용자 별칭(무엇을 재는 채널인지).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>공학단위 ↔ raw 스케일.</summary>
    public AnalogChannelScale Scale { get; init; } = AnalogChannelScale.Default;

    /// <summary>바이트 순서(워드오더). 기본은 XGT 저장 방식인 리틀엔디안.</summary>
    public ByteOrder Order { get; init; } = ByteOrder.Dcba;

    /// <summary>주소를 해석한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    /// <exception cref="InvalidOperationException">비트 주소일 때.</exception>
    public IecAddress Resolve(AddressingOptions? addressing = null)
    {
        var address = IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);
        if (address.Size == DataSize.Bit)
        {
            throw new InvalidOperationException(
                $"비트 주소는 아날로그 채널로 쓸 수 없습니다: {address.Text}");
        }

        return address;
    }

    /// <summary>아날로그 채널로 유효한지 (비트 주소는 제외).</summary>
    public static bool IsValid(string? address)
        => !string.IsNullOrWhiteSpace(address)
            && IecAddress.TryParse(address, out var parsed)
            && parsed.Size != DataSize.Bit;
}
