using Nxs.Core.Time;

namespace Nxs.Core.Tests.Time;

public class TimeSourceTests
{
    [Fact]
    public void SystemTimeSourceReportsWallClockInUtc()
    {
        var before = DateTimeOffset.UtcNow;
        var actual = new SystemTimeSource().UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(actual, before, after);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Fact]
    public async Task SystemTimeSourceDelayActuallyWaits()
    {
        var time = new SystemTimeSource();
        var start = time.UtcNow;

        await time.Delay(TimeSpan.FromMilliseconds(30), CancellationToken.None);

        Assert.True(time.UtcNow - start >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task SystemTimeSourceDelayHonoursCancellation()
    {
        var time = new SystemTimeSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => time.Delay(TimeSpan.FromSeconds(10), cts.Token));
    }

    [Fact]
    public void MonotonicTicksNeverGoBackwards()
    {
        var time = new SystemTimeSource();
        var first = time.MonotonicMilliseconds;

        for (var i = 0; i < 100; i++)
        {
            Assert.True(time.MonotonicMilliseconds >= first);
        }
    }
}
