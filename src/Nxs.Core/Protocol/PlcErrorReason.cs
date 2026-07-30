namespace Nxs.Core.Protocol;

/// <summary>
/// 요청 거절 사유(추상). **와이어 에러 코드가 아니다.**
/// </summary>
/// <remarks>
/// 실장비와 동일한 에러 프레임(PRD X-04)을 내보내려면 이 사유를 XGT 에러 상태 코드로 매핑해야 하는데,
/// 그 코드 표는 spec/xgt-fenet-reference.md 기재 후에만 구현할 수 있다 (⛔ 게이트).
/// </remarks>
public enum PlcErrorReason
{
    /// <summary>성공 — 거절 아님.</summary>
    None = 0,

    /// <summary>주소 표기를 해석할 수 없음.</summary>
    InvalidAddress,

    /// <summary>주소/길이가 영역 경계를 벗어남.</summary>
    RangeExceeded,

    /// <summary>지원하지 않는 데이터 타입/크기 지정자.</summary>
    UnsupportedDataType,

    /// <summary>요청 블록 수가 0이거나 허용 한계를 초과.</summary>
    InvalidBlockCount,

    /// <summary>요청 데이터 길이가 0이거나 허용 한계를 초과.</summary>
    InvalidDataSize,

    /// <summary>쓰기 값 길이가 주소 크기 지정자와 불일치.</summary>
    DataSizeMismatch,
}
