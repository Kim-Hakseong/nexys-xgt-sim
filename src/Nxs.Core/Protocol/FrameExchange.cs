namespace Nxs.Core.Protocol;

/// <summary>한 요청 프레임 처리 결과. 트래픽 로그(PRD X-07)가 그대로 소비한다.</summary>
public sealed record FrameExchange
{
    private static readonly byte[] Empty = [];

    /// <summary>보낼 응답 프레임. 무응답이면 빈 배열.</summary>
    public byte[] ResponseFrame { get; init; } = Empty;

    /// <summary>요청 해석 요약(사람이 읽는 한 줄).</summary>
    public required string RequestSummary { get; init; }

    /// <summary>응답 해석 요약(사람이 읽는 한 줄).</summary>
    public required string ResponseSummary { get; init; }

    /// <summary>거절 사유. 성공이면 <see cref="PlcErrorReason.None"/>.</summary>
    public PlcErrorReason Reason { get; init; } = PlcErrorReason.None;

    /// <summary>
    /// 이 교환이 건드린 주소 표기. 트래픽 로그의 주소 필터가 사용한다.
    /// </summary>
    /// <remarks>
    /// 요약 문자열을 문자열 검색하는 방식은 취약하다 — 코덱이 파싱한 주소를 그대로 넘긴다.
    /// </remarks>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>응답을 보내지 않는 처리 결과인지.</summary>
    public bool IsSilent => ResponseFrame.Length == 0;
}
