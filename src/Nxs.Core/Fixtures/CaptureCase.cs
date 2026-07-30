namespace Nxs.Core.Fixtures;

/// <summary>
/// 캡처 회귀 케이스 한 건 — LabVIEW 가 실제로 보낸 요청 바이트 + 사람이 매뉴얼 대조로 확정한 기대 응답.
/// </summary>
/// <param name="Name">케이스 이름(파일 이름에서 확장자 제외).</param>
/// <param name="RequestBytes">캡처된 요청 바이트(프레임 여러 개를 담을 수 있다).</param>
/// <param name="ExpectedResponse">기대 응답 바이트. <c>.expected</c> 파일이 없으면 null.</param>
public sealed record CaptureCase(string Name, byte[] RequestBytes, byte[]? ExpectedResponse)
{
    /// <summary>기대 응답이 준비되었는지.</summary>
    public bool HasExpectedResponse => ExpectedResponse is not null;
}

/// <summary>재생 판정.</summary>
public enum CaptureReplayOutcome
{
    /// <summary>기대 응답과 일치.</summary>
    Matched,

    /// <summary>기대 응답과 불일치 — 회귀.</summary>
    Mismatched,

    /// <summary>기대 응답 파일이 없어 판정 불가(사람이 작성해야 함).</summary>
    NoExpectedResponse,

    /// <summary>캡처 바이트가 프레이밍 규칙에 맞지 않음.</summary>
    FramingError,

    /// <summary>캡처가 프레임 중간에서 끊김.</summary>
    IncompleteFrame,
}

/// <summary>재생 결과.</summary>
public sealed record CaptureReplayResult
{
    private static readonly byte[] Empty = [];

    /// <summary>케이스 이름.</summary>
    public required string Name { get; init; }

    /// <summary>판정.</summary>
    public required CaptureReplayOutcome Outcome { get; init; }

    /// <summary>시뮬레이터가 실제로 만든 응답.</summary>
    public byte[] ActualResponse { get; init; } = Empty;

    /// <summary>처리한 요청 프레임 수.</summary>
    public int FrameCount { get; init; }

    /// <summary>1바이트씩 주입한 바이트 수(부분 수신 불변 확인용).</summary>
    public int BytesFedOneAtATime { get; init; }

    /// <summary>사람이 읽는 상세 설명.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>회귀로 봐야 하는 결과인지.</summary>
    public bool IsRegression => Outcome is CaptureReplayOutcome.Mismatched
        or CaptureReplayOutcome.FramingError
        or CaptureReplayOutcome.IncompleteFrame;
}
