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

    /// <summary>거절 응답을 만든다.</summary>
    public static PlcResponse Failure(PlcErrorReason reason)
        => reason == PlcErrorReason.None
            ? throw new ArgumentException("거절 응답에 None 사유를 쓸 수 없습니다", nameof(reason))
            : new PlcResponse { Reason = reason };

    /// <summary>블록 없는 성공 응답(쓰기)을 만든다.</summary>
    public static PlcResponse Ok() => new() { Reason = PlcErrorReason.None };

    /// <summary>읽기 결과를 담은 성공 응답을 만든다.</summary>
    public static PlcResponse Ok(IReadOnlyList<byte[]> blocks)
        => new() { Reason = PlcErrorReason.None, Blocks = blocks };
}
