using Nxs.Core.Memory;

namespace Nxs.Core.Protocol;

/// <summary>
/// 프로토콜 중립 PLC 요청. **와이어 포맷이 아니다.**
/// </summary>
/// <remarks>
/// XGT FEnet/Cnet 코덱은 자신의 프레임을 이 모델로 변환한 뒤 <see cref="PlcRequestExecutor"/>에
/// 넘긴다. 프레임 레이아웃·명령 코드·에러 코드는 spec 기재분만 구현한다 (CLAUDE.md §3).
/// </remarks>
public abstract record PlcRequest;

/// <summary>개별 읽기 — 주소 목록마다 한 블록씩 읽는다.</summary>
/// <param name="Addresses">읽을 주소 목록.</param>
public sealed record ReadIndividualRequest(IReadOnlyList<IecAddress> Addresses) : PlcRequest;

/// <summary>개별 쓰기 항목.</summary>
/// <param name="Address">대상 주소.</param>
/// <param name="Value">쓸 값. 길이는 주소 크기 지정자와 일치해야 한다(비트는 1바이트 0/1).</param>
public sealed record PlcWriteItem(IecAddress Address, byte[] Value);

/// <summary>개별 쓰기 — 항목 목록을 모두 적용한다(전부 성공 또는 전부 미적용).</summary>
/// <param name="Items">쓰기 항목 목록.</param>
public sealed record WriteIndividualRequest(IReadOnlyList<PlcWriteItem> Items) : PlcRequest;

/// <summary>연속 읽기 — 시작 주소의 바이트 위치부터 지정 바이트 수를 읽는다.</summary>
/// <param name="Start">시작 주소.</param>
/// <param name="ByteCount">읽을 바이트 수.</param>
public sealed record ReadContinuousRequest(IecAddress Start, int ByteCount) : PlcRequest;

/// <summary>연속 쓰기 — 시작 주소의 바이트 위치부터 데이터를 쓴다.</summary>
/// <param name="Start">시작 주소.</param>
/// <param name="Data">쓸 바이트.</param>
public sealed record WriteContinuousRequest(IecAddress Start, byte[] Data) : PlcRequest;
