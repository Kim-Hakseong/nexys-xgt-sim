namespace Nxs.Core.Protocol;

/// <summary>
/// 바이트 스트림에서 프레임 경계를 결정하는 규칙. "헤더 완독 → 길이만큼 완독" (DESIGN).
/// </summary>
/// <remarks>
/// 이 인터페이스는 프로토콜 중립이다. XGT FEnet 의 실제 헤더 레이아웃·길이 필드 위치는
/// spec/xgt-fenet-reference.md 기재분만 구현한다 (CLAUDE.md §3 조작 제로 원칙).
/// </remarks>
public interface IFrameLengthRule
{
    /// <summary>길이를 판정하기 위해 먼저 완독해야 하는 헤더 바이트 수. 1 이상.</summary>
    int HeaderLength { get; }

    /// <summary>
    /// 완독된 헤더로부터 헤더를 포함한 전체 프레임 길이를 구한다.
    /// </summary>
    /// <param name="header">정확히 <see cref="HeaderLength"/> 바이트.</param>
    /// <param name="totalLength">헤더를 포함한 전체 프레임 길이.</param>
    /// <returns>헤더가 이 프로토콜의 것으로 해석되면 참.</returns>
    bool TryGetTotalLength(ReadOnlySpan<byte> header, out int totalLength);
}
