using System.Diagnostics;

namespace Nxs.Core.Time;

/// <summary>실제 시스템 시계 기반 <see cref="ITimeSource"/>. 운영 기본 구현.</summary>
public sealed class SystemTimeSource : ITimeSource
{
    private static readonly Stopwatch Monotonic = Stopwatch.StartNew();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public long MonotonicMilliseconds => Monotonic.ElapsedMilliseconds;

    /// <inheritdoc />
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
