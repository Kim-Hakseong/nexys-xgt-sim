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

    /// <summary>IEEE754 단정도 실수 (4바이트 — %..D 주소 필요).</summary>
    Float,

    /// <summary>IEEE754 배정도 실수 (8바이트 — %..L 주소 필요).</summary>
    Double,
}

/// <summary>
/// 사용자 지정 워치 항목 — 랙 매핑과 무관한 임의 주소를 직접 보고 쓴다.
/// </summary>
/// <remarks>
/// LabVIEW 는 대부분 <c>%M</c> 영역과 대화하는데 그 주소는 I/O 랙 매핑에 나타나지 않는다.
/// 이 목록이 있어야 <c>%MW320</c>, <c>%MD422</c> 같은 실제 교신 주소를 눈으로 확인하며 티키타카할 수 있다.
/// 값 해석 기준(형식·바이트 순서)을 항목마다 따로 정할 수 있어 마스터와 맞출 수 있다.
/// </remarks>
public sealed record WatchEntry
{
    /// <summary>IEC 주소 표기. 예: <c>%MW320</c>, <c>%MD422</c>, <c>%ML50</c>, <c>%MX801</c>.</summary>
    public required string Address { get; init; }

    /// <summary>사용자 별칭(무엇을 뜻하는 주소인지).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>표시 형식.</summary>
    public WatchFormat Format { get; init; } = WatchFormat.Decimal;

    /// <summary>바이트 순서(워드오더). 기본은 XGT 저장 방식인 리틀엔디안.</summary>
    public ByteOrder Order { get; init; } = ByteOrder.Dcba;

    /// <summary>주소를 해석한다.</summary>
    /// <exception cref="FormatException">표기가 올바르지 않을 때.</exception>
    public IecAddress Resolve(AddressingOptions? addressing = null)
        => IecAddress.Parse(Address, addressing ?? AddressingOptions.Default);

    /// <summary>주소 표기가 유효한지 (목록에 넣기 전 검사).</summary>
    public static bool IsValid(string? address)
        => !string.IsNullOrWhiteSpace(address) && IecAddress.TryParse(address, out _);
}
