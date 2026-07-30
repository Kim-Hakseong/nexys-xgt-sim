using Nxs.Core.Time;

namespace Nxs.TestKit;

/// <summary>결정적 <see cref="ITimeSource"/>. 시각을 테스트가 직접 전진시킨다.</summary>
public sealed class FakeTimeSource : ITimeSource
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    /// <summary>고정 시작 시각으로 만든다.</summary>
    public FakeTimeSource(DateTimeOffset? start = null)
        => _now = start ?? new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    /// <inheritdoc />
    public long MonotonicMilliseconds
    {
        get
        {
            lock (_gate)
            {
                return (long)(_now - DateTimeOffset.UnixEpoch).TotalMilliseconds;
            }
        }
    }

    /// <summary><see cref="Delay"/> 호출로 요청된 지연 목록.</summary>
    public List<TimeSpan> RequestedDelays { get; } = [];

    /// <summary>
    /// 지연을 실제로 기다리지 않고 시각만 전진시킨다.
    /// </summary>
    /// <remarks>
    /// 완료된 Task 를 그대로 반환하면 <c>while(!ct) { work(); await Delay(); }</c> 형태의 루프가
    /// 스케줄러에 양보하지 않아 호출 스레드를 영구 점유한다(취소를 요청할 기회조차 없다).
    /// 그래서 시각만 전진시킨 뒤 반드시 한 번 양보한다.
    /// </remarks>
    public async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RequestedDelays.Add(delay);
            _now += delay;
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>시각을 전진시킨다.</summary>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }
}
