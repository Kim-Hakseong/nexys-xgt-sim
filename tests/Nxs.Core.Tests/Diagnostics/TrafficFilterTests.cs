using Nxs.Core.Diagnostics;
using Nxs.Core.Protocol;

namespace Nxs.Core.Tests.Diagnostics;

/// <summary>
/// 트래픽 로그 필터 — 방향 3가지 × 주소 목록 × 오류 여부.
/// </summary>
public class TrafficFilterTests
{
    private static TrafficEvent Event(
        TrafficDirection direction,
        PlcErrorReason reason = PlcErrorReason.None,
        params string[] addresses) => new()
        {
            Timestamp = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            Direction = direction,
            ClientId = "#1",
            Summary = "테스트",
            Reason = reason,
            Addresses = addresses,
        };

    [Fact]
    public void DefaultFilterAcceptsEverything()
    {
        var filter = TrafficFilter.All;

        Assert.True(filter.Accepts(Event(TrafficDirection.Rx)));
        Assert.True(filter.Accepts(Event(TrafficDirection.Tx)));
        Assert.True(filter.Accepts(Event(TrafficDirection.Note)));
        Assert.False(filter.HasAddressFilter);
    }

    [Fact]
    public void RxOnlyHidesTxAndNotes()
    {
        var filter = new TrafficFilter { Direction = TrafficDirectionFilter.RxOnly };

        Assert.True(filter.Accepts(Event(TrafficDirection.Rx)));
        Assert.False(filter.Accepts(Event(TrafficDirection.Tx)));
        // 방향 없는 알림 행은 "함께" 에서만 보인다 — RX 만 볼 때 잡음이 되지 않게.
        Assert.False(filter.Accepts(Event(TrafficDirection.Note)));
    }

    [Fact]
    public void TxOnlyHidesRxAndNotes()
    {
        var filter = new TrafficFilter { Direction = TrafficDirectionFilter.TxOnly };

        Assert.False(filter.Accepts(Event(TrafficDirection.Rx)));
        Assert.True(filter.Accepts(Event(TrafficDirection.Tx)));
        Assert.False(filter.Accepts(Event(TrafficDirection.Note)));
    }

    [Fact]
    public void AddressFilterKeepsOnlyMatchingRows()
    {
        var filter = new TrafficFilter { Addresses = ["%MW320"] };

        Assert.True(filter.Accepts(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW320")));
        Assert.False(filter.Accepts(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW999")));
        Assert.True(filter.HasAddressFilter);
    }

    [Fact]
    public void AddressFilterMatchesAnyOfSeveralAddressesInOneFrame()
    {
        var filter = new TrafficFilter { Addresses = ["%MW320"] };

        // 한 프레임이 여러 주소를 다루면 하나만 맞아도 보인다.
        Assert.True(filter.Accepts(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW10", "%MW320")));
    }

    [Fact]
    public void AddressFilterIsCaseInsensitive()
    {
        var filter = new TrafficFilter { Addresses = ["%mw320"] };

        Assert.True(filter.Accepts(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW320")));
    }

    [Fact]
    public void AddressFilterHidesRowsWithNoAddressInformation()
    {
        var filter = new TrafficFilter { Addresses = ["%MW320"] };

        // 연결 알림처럼 주소를 모르는 행은 특정 주소만 볼 때 숨긴다.
        Assert.False(filter.Accepts(Event(TrafficDirection.Note)));
    }

    [Fact]
    public void ErrorsOnlyCombinesWithDirectionAndAddress()
    {
        var filter = new TrafficFilter
        {
            ErrorsOnly = true,
            Direction = TrafficDirectionFilter.TxOnly,
            Addresses = ["%MW320"],
        };

        Assert.True(filter.Accepts(Event(TrafficDirection.Tx, PlcErrorReason.RangeExceeded, "%MW320")));
        Assert.False(filter.Accepts(Event(TrafficDirection.Tx, PlcErrorReason.None, "%MW320")));
        Assert.False(filter.Accepts(Event(TrafficDirection.Rx, PlcErrorReason.RangeExceeded, "%MW320")));
        Assert.False(filter.Accepts(Event(TrafficDirection.Tx, PlcErrorReason.RangeExceeded, "%MW1")));
    }

    [Fact]
    public void LogSnapshotAppliesTheFilter()
    {
        var log = new TrafficLog();
        log.Record(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW320"));
        log.Record(Event(TrafficDirection.Tx, PlcErrorReason.None, "%MW320"));
        log.Record(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW999"));
        log.Record(Event(TrafficDirection.Note));

        Assert.Equal(4, log.Snapshot().Count);
        Assert.Equal(2, log.Snapshot(new TrafficFilter { Addresses = ["%MW320"] }).Count);
        Assert.Equal(2, log.Snapshot(new TrafficFilter { Direction = TrafficDirectionFilter.RxOnly }).Count);
        Assert.Single(log.Snapshot(new TrafficFilter
        {
            Direction = TrafficDirectionFilter.TxOnly,
            Addresses = ["%MW320"],
        }));
    }

    [Fact]
    public void SavingWithAFilterWritesOnlyMatchingRowsAndIncludesTheAddressColumn()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-tf-").FullName;
        try
        {
            var log = new TrafficLog();
            log.Record(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW320"));
            log.Record(Event(TrafficDirection.Rx, PlcErrorReason.None, "%MW999"));
            var path = Path.Combine(dir, "filtered.log");

            log.Save(path, new TrafficFilter { Addresses = ["%MW320"] });
            var text = File.ReadAllText(path);

            Assert.Contains("%MW320", text, StringComparison.Ordinal);
            Assert.DoesNotContain("%MW999", text, StringComparison.Ordinal);
            Assert.Contains("주소", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DirectionLabelsAreDistinctAndKorean()
    {
        Assert.Equal("RX + TX 함께", TrafficDirectionFilter.RxAndTx.Label());
        Assert.Equal("RX 만 (마스터 → 시뮬)", TrafficDirectionFilter.RxOnly.Label());
        Assert.Equal("TX 만 (시뮬 → 마스터)", TrafficDirectionFilter.TxOnly.Label());
    }
}
