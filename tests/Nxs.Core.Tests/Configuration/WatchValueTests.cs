using Nxs.Core.Configuration;

namespace Nxs.Core.Tests.Configuration;

/// <summary>
/// 워치 값 해석 — 바이트 순서(워드오더) × 표시 형식.
/// </summary>
/// <remarks>
/// 엔디안은 본질적으로 바이트 순서 문제이므로 메모리 바이트를 직접 다룬다.
/// 기준을 LabVIEW 와 맞추기 위해 Modbus 관례(ABCD/DCBA/BADC/CDAB)를 따른다.
/// </remarks>
public class WatchValueTests
{
    // 메모리에 놓인 바이트 (오프셋 낮은 쪽부터): A B C D
    private static readonly byte[] Abcd = [0x12, 0x34, 0x56, 0x78];

    [Fact]
    public void AbcdIsBigEndianSoTheFirstMemoryByteIsMostSignificant()
        => Assert.Equal("0x12345678", WatchValue.Render(Abcd, WatchFormat.Hex, ByteOrder.Abcd));

    [Fact]
    public void DcbaIsLittleEndianAndMatchesTheExistingReadScalarBehaviour()
        => Assert.Equal("0x78563412", WatchValue.Render(Abcd, WatchFormat.Hex, ByteOrder.Dcba));

    [Fact]
    public void BadcSwapsBytesWithinEachWord()
        => Assert.Equal("0x34127856", WatchValue.Render(Abcd, WatchFormat.Hex, ByteOrder.Badc));

    [Fact]
    public void CdabSwapsWordOrder()
        => Assert.Equal("0x56781234", WatchValue.Render(Abcd, WatchFormat.Hex, ByteOrder.Cdab));

    [Fact]
    public void TwoByteValueOnlyHasTwoMeaningfulOrders()
    {
        byte[] ab = [0x12, 0x34];

        Assert.Equal("0x1234", WatchValue.Render(ab, WatchFormat.Hex, ByteOrder.Abcd));
        Assert.Equal("0x3412", WatchValue.Render(ab, WatchFormat.Hex, ByteOrder.Dcba));
    }

    [Fact]
    public void DecimalAndSignedUseTheSelectedOrder()
    {
        byte[] bytes = [0xFF, 0xFF];

        Assert.Equal("65535", WatchValue.Render(bytes, WatchFormat.Decimal, ByteOrder.Dcba));
        Assert.Equal("-1", WatchValue.Render(bytes, WatchFormat.Signed, ByteOrder.Dcba));
    }

    [Fact]
    public void SignedRespectsWidth()
    {
        Assert.Equal("-2", WatchValue.Render([0xFE, 0xFF, 0xFF, 0xFF], WatchFormat.Signed, ByteOrder.Dcba));
        Assert.Equal("32767", WatchValue.Render([0xFF, 0x7F], WatchFormat.Signed, ByteOrder.Dcba));
    }

    [Fact]
    public void BinaryRendersEveryBitGroupedByFour()
        => Assert.Equal("0001 0010 0011 0100", WatchValue.Render([0x34, 0x12], WatchFormat.Binary, ByteOrder.Dcba));

    [Fact]
    public void BoolIsOnWhenAnyBitIsSet()
    {
        Assert.Equal("ON", WatchValue.Render([0x01], WatchFormat.Bool, ByteOrder.Dcba));
        Assert.Equal("OFF", WatchValue.Render([0x00], WatchFormat.Bool, ByteOrder.Dcba));
    }

    // ==================== Float (Single, 4바이트) ====================

    [Fact]
    public void FloatRoundTripsThroughLittleEndianBytes()
    {
        // 3.14159274f 의 IEEE754 비트 = 0x40490FDB
        byte[] littleEndian = [0xDB, 0x0F, 0x49, 0x40];

        var text = WatchValue.Render(littleEndian, WatchFormat.Float, ByteOrder.Dcba);

        Assert.StartsWith("3.14159", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatRoundTripsThroughBigEndianBytes()
    {
        byte[] bigEndian = [0x40, 0x49, 0x0F, 0xDB];

        var text = WatchValue.Render(bigEndian, WatchFormat.Float, ByteOrder.Abcd);

        Assert.StartsWith("3.14159", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatWordSwappedOrderIsSupported()
    {
        // CDAB: 워드가 뒤바뀐 배치 — 0x40490FDB 를 CDAB 로 담으면 0F DB 40 49
        byte[] wordSwapped = [0x0F, 0xDB, 0x40, 0x49];

        Assert.StartsWith("3.14159", WatchValue.Render(wordSwapped, WatchFormat.Float, ByteOrder.Cdab),
            StringComparison.Ordinal);
    }

    [Fact]
    public void FloatNeedsFourBytes()
    {
        var text = WatchValue.Render([0x00, 0x00], WatchFormat.Float, ByteOrder.Dcba);

        Assert.Contains("4바이트", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatParseWritesTheCorrectBytes()
    {
        var bytes = WatchValue.Parse("3.14159274", 4, WatchFormat.Float, ByteOrder.Dcba);

        Assert.NotNull(bytes);
        Assert.Equal("DB 0F 49 40", Nxs.Core.Protocol.Hex.Format(bytes));
    }

    [Fact]
    public void FloatParseAndRenderRoundTripAcrossOrders()
    {
        foreach (var order in new[] { ByteOrder.Abcd, ByteOrder.Dcba, ByteOrder.Badc, ByteOrder.Cdab })
        {
            var bytes = WatchValue.Parse("-273.15", 4, WatchFormat.Float, order);
            Assert.NotNull(bytes);
            Assert.StartsWith("-273.15", WatchValue.Render(bytes!, WatchFormat.Float, order),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FloatHandlesNegativeAndFractionalValues()
    {
        var bytes = WatchValue.Parse("-0.125", 4, WatchFormat.Float, ByteOrder.Dcba);

        Assert.Equal("-0.125", WatchValue.Render(bytes!, WatchFormat.Float, ByteOrder.Dcba));
    }

    // ==================== Double (8바이트) ====================

    [Fact]
    public void DoubleRoundTripsAcrossOrders()
    {
        foreach (var order in new[] { ByteOrder.Abcd, ByteOrder.Dcba, ByteOrder.Badc, ByteOrder.Cdab })
        {
            var bytes = WatchValue.Parse("3.141592653589793", 8, WatchFormat.Double, order);
            Assert.NotNull(bytes);
            Assert.Equal(8, bytes!.Length);
            Assert.StartsWith("3.14159265358979", WatchValue.Render(bytes, WatchFormat.Double, order),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DoubleNeedsEightBytes()
        => Assert.Contains("8바이트", WatchValue.Render([0x00, 0x00, 0x00, 0x00], WatchFormat.Double, ByteOrder.Dcba),
            StringComparison.Ordinal);

    [Fact]
    public void DoubleKeepsFullPrecision()
    {
        var bytes = WatchValue.Parse("1234567.891011", 8, WatchFormat.Double, ByteOrder.Dcba);

        Assert.Equal("1234567.891011", WatchValue.Render(bytes!, WatchFormat.Double, ByteOrder.Dcba));
    }

    // ==================== 입력 파싱 ====================

    [Fact]
    public void IntegerParseAcceptsDecimalHexAndNegative()
    {
        Assert.Equal("34 12", Nxs.Core.Protocol.Hex.Format(
            WatchValue.Parse("4660", 2, WatchFormat.Decimal, ByteOrder.Dcba)!));
        Assert.Equal("34 12", Nxs.Core.Protocol.Hex.Format(
            WatchValue.Parse("0x1234", 2, WatchFormat.Hex, ByteOrder.Dcba)!));
        Assert.Equal("FF FF", Nxs.Core.Protocol.Hex.Format(
            WatchValue.Parse("-1", 2, WatchFormat.Signed, ByteOrder.Dcba)!));
    }

    [Fact]
    public void BigEndianParsePlacesTheMostSignificantByteFirst()
        => Assert.Equal("12 34", Nxs.Core.Protocol.Hex.Format(
            WatchValue.Parse("0x1234", 2, WatchFormat.Hex, ByteOrder.Abcd)!));

    [Fact]
    public void BoolParseAcceptsOnOffAndTrueFalse()
    {
        Assert.Equal("01", Nxs.Core.Protocol.Hex.Format(WatchValue.Parse("ON", 1, WatchFormat.Bool, ByteOrder.Dcba)!));
        Assert.Equal("00", Nxs.Core.Protocol.Hex.Format(WatchValue.Parse("off", 1, WatchFormat.Bool, ByteOrder.Dcba)!));
        Assert.Equal("01", Nxs.Core.Protocol.Hex.Format(WatchValue.Parse("true", 1, WatchFormat.Bool, ByteOrder.Dcba)!));
    }

    [Fact]
    public void ParseRejectsGarbageAndOverflow()
    {
        Assert.Null(WatchValue.Parse("헛소리", 2, WatchFormat.Decimal, ByteOrder.Dcba));
        Assert.Null(WatchValue.Parse("", 2, WatchFormat.Decimal, ByteOrder.Dcba));
        Assert.Null(WatchValue.Parse("70000", 2, WatchFormat.Decimal, ByteOrder.Dcba));
        Assert.Null(WatchValue.Parse("abc", 4, WatchFormat.Float, ByteOrder.Dcba));
    }

    [Fact]
    public void ParseRejectsFloatOnAWrongWidth()
    {
        Assert.Null(WatchValue.Parse("1.5", 2, WatchFormat.Float, ByteOrder.Dcba));
        Assert.Null(WatchValue.Parse("1.5", 4, WatchFormat.Double, ByteOrder.Dcba));
    }

    [Fact]
    public void EveryOrderIsItsOwnInverse()
    {
        // 같은 순서로 두 번 변환하면 원래 바이트로 돌아온다 (모든 순서가 대합적 치환이다).
        foreach (var order in new[] { ByteOrder.Abcd, ByteOrder.Dcba, ByteOrder.Badc, ByteOrder.Cdab })
        {
            var once = WatchValue.ToMsbFirst(Abcd, order);
            var twice = WatchValue.FromMsbFirst(once, order);
            Assert.Equal(Abcd, twice);
        }
    }

    [Fact]
    public void OrderLabelsMatchTheSisterProjectConvention()
    {
        Assert.Equal("ABCD (빅엔디안)", ByteOrder.Abcd.Label());
        Assert.Equal("DCBA (리틀엔디안)", ByteOrder.Dcba.Label());
        Assert.Equal("BADC (바이트 스왑)", ByteOrder.Badc.Label());
        Assert.Equal("CDAB (워드 스왑)", ByteOrder.Cdab.Label());
    }
}
