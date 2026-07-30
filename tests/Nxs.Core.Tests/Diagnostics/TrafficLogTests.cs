using Nxs.Core.Diagnostics;
using Nxs.Core.Protocol;

namespace Nxs.Core.Tests.Diagnostics;

/// <summary>PRD X-07 — RX/TX raw hex + 해석 요약 + 타임스탬프, 에러 필터, 파일 저장.</summary>
public class TrafficLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nxsim-log-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static TrafficEvent Event(
        TrafficDirection direction,
        byte[]? raw = null,
        string summary = "",
        PlcErrorReason reason = PlcErrorReason.None,
        int second = 0) => new()
        {
            Timestamp = new DateTimeOffset(2026, 7, 30, 12, 0, second, TimeSpan.Zero),
            Direction = direction,
            ClientId = "#1 127.0.0.1:5000",
            Raw = raw ?? [],
            Summary = summary,
            Reason = reason,
        };

    [Fact]
    public void RecordedEventsComeBackInOrder()
    {
        var log = new TrafficLog();

        log.Record(Event(TrafficDirection.Rx, [0x01], "요청", second: 1));
        log.Record(Event(TrafficDirection.Tx, [0x02], "응답", second: 2));

        var events = log.Snapshot();
        Assert.Equal(2, events.Count);
        Assert.Equal(TrafficDirection.Rx, events[0].Direction);
        Assert.Equal(TrafficDirection.Tx, events[1].Direction);
    }

    [Fact]
    public void EventExposesRawHexAndTimestamp()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0x00, 0xAB, 0xFF], "개별 읽기 %MW0"));

        var e = Assert.Single(log.Snapshot());
        Assert.Equal("00 AB FF", e.RawHex);
        Assert.Equal("개별 읽기 %MW0", e.Summary);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero), e.Timestamp);
    }

    [Fact]
    public void ErrorFilterReturnsOnlyErrorRows()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0x01], "정상"));
        log.Record(Event(TrafficDirection.Tx, [0x02], "거절", PlcErrorReason.RangeExceeded));
        log.Record(Event(TrafficDirection.Rx, [0x03], "정상"));

        var errors = log.Snapshot(errorsOnly: true);

        Assert.Equal(PlcErrorReason.RangeExceeded, Assert.Single(errors).Reason);
        Assert.Equal(3, log.Snapshot().Count);
    }

    [Fact]
    public void ErrorCountTracksErrorRows()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0x01], "정상"));
        log.Record(Event(TrafficDirection.Tx, [0x02], "거절", PlcErrorReason.InvalidAddress));

        Assert.Equal(1, log.ErrorCount);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void OldestEventsAreDroppedWhenCapacityIsExceeded()
    {
        var log = new TrafficLog(capacity: 3);

        for (var i = 0; i < 5; i++)
        {
            log.Record(Event(TrafficDirection.Rx, [(byte)i], $"#{i}", second: i));
        }

        var events = log.Snapshot();
        Assert.Equal(3, events.Count);
        Assert.Equal("#2", events[0].Summary);
        Assert.Equal("#4", events[2].Summary);
        Assert.Equal(2, log.DroppedCount);
    }

    [Fact]
    public void ClearEmptiesTheLogAndCounters()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Tx, [0x01], "거절", PlcErrorReason.RangeExceeded));

        log.Clear();

        Assert.Empty(log.Snapshot());
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.ErrorCount);
        Assert.Equal(0, log.DroppedCount);
    }

    [Fact]
    public void SaveWritesTimestampDirectionSummaryAndHex()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0xAA, 0x55], "개별 읽기 %MW10", second: 3));
        log.Record(Event(TrafficDirection.Tx, [0x01], "거절 · RangeExceeded", PlcErrorReason.RangeExceeded, 4));
        var path = Path.Combine(_dir, "traffic.log");

        log.Save(path);
        var text = File.ReadAllText(path);

        Assert.Contains("2026-07-30", text, StringComparison.Ordinal);
        Assert.Contains("RX", text, StringComparison.Ordinal);
        Assert.Contains("TX", text, StringComparison.Ordinal);
        Assert.Contains("AA 55", text, StringComparison.Ordinal);
        Assert.Contains("개별 읽기 %MW10", text, StringComparison.Ordinal);
        Assert.Contains("RangeExceeded", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveCanFilterToErrorsOnly()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0xAA], "정상 요청"));
        log.Record(Event(TrafficDirection.Tx, [0x01], "거절", PlcErrorReason.RangeExceeded));
        var path = Path.Combine(_dir, "errors.log");

        log.Save(path, errorsOnly: true);
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("정상 요청", text, StringComparison.Ordinal);
        Assert.Contains("거절", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveCreatesMissingDirectories()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0x01], "요청"));
        var path = Path.Combine(_dir, "nested", "deeper", "traffic.log");

        log.Save(path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SavingAnEmptyLogProducesAHeaderOnlyFile()
    {
        var path = Path.Combine(_dir, "empty.log");

        new TrafficLog().Save(path);

        Assert.True(File.Exists(path));
        Assert.Contains("Nexys XGT Simulator", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentRecordingLosesNothing()
    {
        var log = new TrafficLog(capacity: 10_000);
        const int perLane = 500;

        Parallel.For(0, 8, lane =>
        {
            for (var i = 0; i < perLane; i++)
            {
                log.Record(Event(TrafficDirection.Rx, [(byte)lane], $"lane{lane}-{i}"));
            }
        });

        Assert.Equal(8 * perLane, log.Count);
        Assert.Equal(8 * perLane, log.Snapshot().Count);
    }

    [Fact]
    public void SnapshotIsAnIsolatedCopy()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, [0x01], "첫 번째"));

        var before = log.Snapshot();
        log.Record(Event(TrafficDirection.Rx, [0x02], "두 번째"));

        Assert.Single(before);
        Assert.Equal(2, log.Snapshot().Count);
    }

    [Fact]
    public void ZeroOrNegativeCapacityIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficLog(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficLog(capacity: -1));
    }
}
