namespace Nxs.Core.Protocol;

/// <summary>
/// 요청 한계값. DESIGN: "연속 읽기 최대 크기·개별 읽기 최대 블록 수 등 한계값도 spec 기재".
/// </summary>
/// <remarks>
/// [미정] spec/xgt-fenet-reference.md 에 한계값이 없으므로 기본은 <c>null</c>(무제한)이다.
/// 시뮬레이터가 실장비에 없는 거절을 발명하면 LabVIEW 검증에 거짓 실패가 생기므로,
/// 근거 없는 상한을 넣지 않는다. spec 확정 시 기본값만 채우면 된다.
/// </remarks>
public sealed record PlcRequestLimits
{
    /// <summary>개별 읽기/쓰기 최대 블록 수. null이면 무제한.</summary>
    public int? MaxIndividualBlocks { get; init; }

    /// <summary>연속 읽기/쓰기 최대 바이트 수. null이면 무제한.</summary>
    public int? MaxContinuousBytes { get; init; }

    /// <summary>기본값(전부 무제한) 인스턴스.</summary>
    public static PlcRequestLimits Default { get; } = new();
}
