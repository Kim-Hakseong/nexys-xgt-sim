using System.Globalization;
using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>
/// 사용자 지정 디지털 점 — 임의 주소의 비트를 토글하거나 감시한다.
/// </summary>
/// <remarks>
/// <para>
/// 비트 주소(<c>%MX801</c>)뿐 아니라 바이트/워드/더블워드/롱워드 주소도 받는다.
/// 워드 주소를 넣으면 그 워드의 16비트가 **배열로 펼쳐져** 각 비트를 개별로 켜고 끌 수 있다
/// (<c>%MB</c>=8 · <c>%MW</c>=16 · <c>%MD</c>=32 · <c>%ML</c>=64개).
/// </para>
/// <para>
/// **모든 점은 양방향이다.** 사용자가 토글하면 메모리에 써서 마스터가 읽고, 마스터가 쓰면
/// 표시가 따라온다. 입력/출력을 나누지 않는다 — 시뮬레이터에서는 사람이 PLC 프로그램 역할까지
/// 하므로 %I·%Q·%M 어느 영역이든 양쪽 방향이 다 필요하다.
/// </para>
/// </remarks>
public sealed record DigitalPointEntry
{
    /// <summary>
    /// IEC 주소 표기. 비트(<c>%MX801</c>) 또는 바이트/워드/더블워드/롱워드(<c>%MW320</c>, <c>%MD422</c>).
    /// </summary>
    public required string Address { get; init; }

    /// <summary>사용자 별칭.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>주소를 해석한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    public IecAddress Resolve(AddressingOptions? addressing = null)
        => IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);

    /// <summary>주소로 유효한지 (목록에 넣기 전 검사).</summary>
    public static bool IsValid(string? address)
        => !string.IsNullOrWhiteSpace(address) && IecAddress.TryParse(address, out _);

    /// <summary>이 주소가 펼쳐지는 비트 수. 비트 주소는 1.</summary>
    public static int BitCountOf(IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Size == DataSize.Bit ? 1 : address.Size.BitWidth();
    }

    /// <summary>
    /// 펼쳐진 비트 하나의 절대 비트 주소를 만든다.
    /// </summary>
    /// <param name="address">그룹 주소.</param>
    /// <param name="bitIndex">그룹 안의 비트 번호(0 = 최하위).</param>
    /// <remarks>
    /// 저장이 리틀엔디안이므로 비트 0 이 시작 바이트의 최하위 비트다
    /// (<c>%MW0</c> 비트0 = <c>%MX0</c> — M1 골든 벡터와 일치).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">비트 번호가 범위를 벗어났을 때.</exception>
    public static IecAddress BitAddressOf(IecAddress address, int bitIndex)
    {
        ArgumentNullException.ThrowIfNull(address);

        var count = BitCountOf(address);
        if (bitIndex < 0 || bitIndex >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitIndex), bitIndex, $"비트 번호는 0..{count - 1} 범위여야 합니다");
        }

        var absolute = address.Size == DataSize.Bit
            ? address.Offset
            : (address.ByteStart * 8) + bitIndex;

        var letter = address.Area switch
        {
            MemoryArea.I => 'I',
            MemoryArea.Q => 'Q',
            MemoryArea.M => 'M',
            _ => throw new ArgumentOutOfRangeException(nameof(address), address.Area, "알 수 없는 영역"),
        };

        return IecAddress.Parse(
            string.Create(CultureInfo.InvariantCulture, $"%{letter}X{absolute}"));
    }
}
