namespace Nxs.Core.Protocol.Xgt;

/// <summary>개별 쓰기 요청에서 이름과 데이터의 배치 방식.</summary>
/// <remarks>
/// spec 초안 §3 신뢰도 '낮음' — BlockCount=1 이면 두 배치가 바이트열로 동일하므로 실용 위험은 낮다.
/// 2블록 이상 쓰기를 쓰는 마스터를 만나면 이 설정으로 바꾼다.
/// </remarks>
public enum XgtWriteBlockLayout
{
    /// <summary>이름 전부 → 데이터 전부 (본 구현 기본).</summary>
    Grouped,

    /// <summary>블록마다 이름+데이터 교차.</summary>
    Interleaved,
}

/// <summary>
/// XGT FEnet 코덱 설정. **초안에서 신뢰도가 낮은 항목을 전부 여기로 노출**한다.
/// </summary>
/// <remarks>
/// 재컴파일 없이 실장비와 맞출 수 있게 하는 것이 목적이다 —
/// 미검증 값을 코드에 못박아 두면 틀렸을 때 고치기 어렵다.
/// </remarks>
public sealed record XgtFenetOptions
{
    /// <summary>XGT 전용 프로토콜 기본 TCP 포트 (초안 §6, 신뢰도 높음).</summary>
    public const int DefaultPort = 2004;

    /// <summary>
    /// 수신 프레임의 BCC 를 검사할지. 기본 <c>false</c>.
    /// </summary>
    /// <remarks>
    /// 초안 §1 에서 BCC 계산 범위가 신뢰도 '낮음' 이다. 틀린 범위로 검사하면 정상 요청을 전부
    /// 거절해 접속이 아예 안 되는 것처럼 보인다 — 관용적 기본이 안전하다.
    /// 범위가 확정되면 true 로 바꿔 실장비와 동일하게 엄격히 거절하게 한다.
    /// </remarks>
    public bool ValidateInboundBcc { get; init; }

    /// <summary>수신 프레임의 Company ID 를 검사할지. 기본 <c>true</c>(쓰레기 바이트 조기 검출).</summary>
    public bool ValidateCompanyId { get; init; } = true;

    /// <summary>개별 쓰기 요청의 블록 배치.</summary>
    public XgtWriteBlockLayout WriteBlockLayout { get; init; } = XgtWriteBlockLayout.Grouped;

    /// <summary>
    /// 추상 거절 사유 → 와이어 에러 코드 매핑. 지정하지 않은 사유는 기본 잠정값을 쓴다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 초안 §5 의 기본값은 **거의 확실히 틀렸다**. 매뉴얼의 에러 상태 코드 표로 교체할 것.
    /// </remarks>
    public IReadOnlyDictionary<PlcErrorReason, ushort>? ErrorCodeMap { get; init; }

    /// <summary>요청 한계값(개별 블록 수·연속 바이트 수). 기본 무제한.</summary>
    public PlcRequestLimits Limits { get; init; } = PlcRequestLimits.Default;

    /// <summary>허용 최대 프레임 길이(헤더 포함).</summary>
    public int MaxFrameLength { get; init; } = 8192;

    /// <summary>기본값 인스턴스.</summary>
    public static XgtFenetOptions Default { get; } = new();
}
