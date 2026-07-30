namespace Nxs.Core.Protocol;

/// <summary>
/// 와이어 프레임 ↔ PLC 동작 변환기. 프로토콜 세부를 전부 이 안에 가둔다.
/// </summary>
/// <remarks>
/// <para>
/// 서버(<c>Nxs.Core.Server</c>)는 이 인터페이스만 알기 때문에 전송 계층에 프로토콜 지식이 없다.
/// 헤더 레이아웃·명령 코드·데이터 타입 코드·에러 코드 표·Invoke/Frame ID 에코 규칙은
/// **전부 구현체의 책임**이며, spec/xgt-fenet-reference.md 기재분만 구현한다 (CLAUDE.md §3).
/// </para>
/// <para>
/// ⛔ 현재 XGT FEnet 구현체는 없다 — spec 파일에 프레임 근거가 기재되지 않았기 때문이다.
/// 근거가 채워지면 이 인터페이스를 구현하는 클래스 하나만 추가하면 서버는 그대로 동작한다.
/// </para>
/// </remarks>
public interface IFrameCodec
{
    /// <summary>수신 스트림에서 프레임 경계를 찾는 규칙.</summary>
    IFrameLengthRule LengthRule { get; }

    /// <summary>허용 최대 프레임 길이(헤더 포함).</summary>
    int MaxFrameLength { get; }

    /// <summary>완독된 요청 프레임 하나를 처리하고 응답 프레임을 만든다.</summary>
    /// <param name="requestFrame">완독된 요청 프레임 전체.</param>
    /// <returns>응답 프레임과 로그 요약.</returns>
    FrameExchange Handle(ReadOnlySpan<byte> requestFrame);
}
