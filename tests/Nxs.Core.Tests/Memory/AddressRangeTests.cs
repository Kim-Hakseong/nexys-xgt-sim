using Nxs.Core.Memory;
using Xunit;

namespace Nxs.Core.Tests.Memory;

/// <summary>시작 주소 + 개수를 연속 주소로 펼치는 규칙.</summary>
public class AddressRangeTests
{
    [Fact]
    public void IncrementIsInNotationUnitsNotBytes()
    {
        // %MW100 다음은 %MW101 이다 — 바이트로 환산해 %MW102 가 되면 화면 번지와 어긋난다.
        var range = AddressRange.Expand("%MW100", 3);

        Assert.Equal(["%MW100", "%MW101", "%MW102"], range.Select(a => a.Text));
        Assert.Equal([200, 202, 204], range.Select(a => a.ByteStart));
    }

    [Theory]
    [InlineData("%MX0", "%MX0", "%MX4")]
    [InlineData("%MB40", "%MB40", "%MB44")]
    [InlineData("%MD10", "%MD10", "%MD14")]
    [InlineData("%ML2", "%ML2", "%ML6")]
    [InlineData("%IW80", "%IW80", "%IW84")]
    [InlineData("%QX1024", "%QX1024", "%QX1028")]
    public void EverySizeAndAreaExpandsInItsOwnUnit(string start, string first, string last)
    {
        var range = AddressRange.Expand(start, 5);

        Assert.Equal(5, range.Count);
        Assert.Equal(first, range[0].Text);
        Assert.Equal(last, range[^1].Text);
        Assert.All(range, a => Assert.Equal(range[0].Area, a.Area));
        Assert.All(range, a => Assert.Equal(range[0].Size, a.Size));
    }

    [Fact]
    public void SingleCountIsJustTheStartAddress()
        => Assert.Equal(["%MW7"], AddressRange.Expand("%MW7", 1).Select(a => a.Text));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AddressRange.MaxCount + 1)]
    public void CountOutsideTheAllowedBandIsRejected(int count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AddressRange.Expand("%MW0", count));

    [Fact]
    public void TheMaximumCountItselfIsAllowed()
        => Assert.Equal(AddressRange.MaxCount, AddressRange.Expand("%MB0", AddressRange.MaxCount).Count);

    [Fact]
    public void UnparsableStartAddressIsReportedAsAFormatError()
        => Assert.Throws<FormatException>(() => AddressRange.Expand("MW", 5));

    [Fact]
    public void RunningPastTheEndOfMemoryIsReportedWithBothNumbers()
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 64 });

        // %MW30 은 바이트 60..61 — 4개면 %MW33(바이트 66..67)이라 영역을 넘는다.
        var ex = Assert.Throws<InvalidOperationException>(
            () => AddressRange.Expand("%MW30", 4, memory: memory));

        Assert.Contains("%MW30", ex.Message, StringComparison.Ordinal);
        Assert.Contains("64", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeThatExactlyFillsMemoryIsAllowed()
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 64 });

        var range = AddressRange.Expand("%MW0", 32, memory: memory);

        Assert.Equal(32, range.Count);
        Assert.Equal(64, range[^1].ByteEnd);
    }

    [Fact]
    public void WithoutAMemoryNoRangeCheckIsPerformed()
    {
        // 메모리를 주지 않으면 순수 주소 계산만 한다 — 호출부가 검사 시점을 고를 수 있어야 한다.
        var range = AddressRange.Expand("%MW60000", 2);
        Assert.Equal(2, range.Count);
    }

    [Theory]
    [InlineData("%MW0", 100, true)]
    [InlineData("%MW0", 0, false)]
    [InlineData("%MW0", AddressRange.MaxCount + 1, false)]
    [InlineData("MW", 10, false)]
    [InlineData(null, 10, false)]
    [InlineData("   ", 10, false)]
    public void CanExpandAnswersWithoutThrowing(string? start, int count, bool expected)
        => Assert.Equal(expected, AddressRange.CanExpand(start, count));
}
