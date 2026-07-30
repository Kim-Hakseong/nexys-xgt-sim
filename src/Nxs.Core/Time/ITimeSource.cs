namespace Nxs.Core.Time;

/// <summary>
/// 시간 의존성 주입 지점 (CLAUDE.md §4.2 — 시간 로직은 ITimeSource 주입).
/// 타임스탬프·주기·지연을 테스트에서 결정적으로 다루기 위한 것이다.
/// </summary>
public interface ITimeSource
{
    /// <summary>현재 UTC 시각. 트래픽 로그 타임스탬프에 쓴다.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>단조 증가 밀리초. 경과 시간 측정·자동화 tick 계산용(월클럭 점프에 영향받지 않는다).</summary>
    long MonotonicMilliseconds { get; }

    /// <summary>지정 시간만큼 대기한다.</summary>
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}
