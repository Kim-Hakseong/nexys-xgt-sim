namespace Nxs.Core.Protocol;

/// <summary>프로토콜 중립 PLC 응답. **와이어 포맷이 아니다.**</summary>
public sealed record PlcResponse
{
    private static readonly byte[][] NoBlocks = [];

    /// <summary>요청이 수락되었는지.</summary>
    public bool IsSuccess => Reason == PlcErrorReason.None;

    /// <summary>거절 사유. 성공 시 <see cref="PlcErrorReason.None"/>.</summary>
    public required PlcErrorReason Reason { get; init; }

    /// <summary>읽기 결과 블록. 쓰기·거절 시 빈 목록.</summary>
    public IReadOnlyList<byte[]> Blocks { get; init; } = NoBlocks;

    /// <summary>
    /// 거절을 사람이 읽을 수 있게 설명한 한 줄. 성공이면 빈 문자열.
    /// </summary>
    /// <remarks>
    /// 사유 열거형만으로는 현장에서 진단이 안 된다 — "DataSizeMismatch" 만 보고는
    /// 무엇과 무엇이 안 맞는지 알 수 없어 프레임을 다시 받아 봐야 했다.
    /// 판정한 쪽이 그 자리에서 숫자를 적어 두면 트래픽 로그 한 줄로 원인이 드러난다.
    /// </remarks>
    public string Detail { get; init; } = string.Empty;

    /// <summary>거절 응답을 만든다.</summary>
    /// <param name="reason">거절 사유.</param>
    /// <param name="detail">무엇이 왜 거절됐는지 — 숫자를 포함해 적는다.</param>
    public static PlcResponse Failure(PlcErrorReason reason, string detail = "")
        => reason == PlcErrorReason.None
            ? throw new ArgumentException("거절 응답에 None 사유를 쓸 수 없습니다", nameof(reason))
            : new PlcResponse { Reason = reason, Detail = detail };

    /// <summary>블록 없는 성공 응답(쓰기)을 만든다.</summary>
    public static PlcResponse Ok() => new() { Reason = PlcErrorReason.None };

    /// <summary>읽기 결과를 담은 성공 응답을 만든다.</summary>
    public static PlcResponse Ok(IReadOnlyList<byte[]> blocks)
        => new() { Reason = PlcErrorReason.None, Blocks = blocks };
}
