using Nxs.Core.Memory;
using Xunit;

namespace Nxs.Core.Tests.Memory;

/// <summary>
/// 주소 묶음 — 한쪽에 값이 들어가면 같은 묶음의 나머지가 따라온다.
/// </summary>
/// <remarks>
/// 실장비에서는 PLC 프로그램이 "MW0 을 MW1 에 복사" 같은 로직을 돌리는데 시뮬레이터에는
/// 그 프로그램이 없다. 묶음이 그 자리를 메운다.
/// </remarks>
public class MemoryLinkTests
{
    private static PlcMemory Linked(params string[][] groups)
    {
        var memory = new PlcMemory();
        memory.Links = new MemoryLinks(
            groups.Select(g => new ResolvedLinkGroup(
                g.Select(a => BitNotation.Parse(a)).ToArray())).ToArray());
        return memory;
    }

    private static uint Read(PlcMemory memory, string address)
        => memory.ReadScalar(IecAddress.Parse(address));

    // ==================== 워드 묶음 ====================

    [Fact]
    public void WritingOneWordCopiesItToTheOther()
    {
        var memory = Linked(["%MW0", "%MW1"]);

        memory.WriteScalar(IecAddress.Parse("%MW0"), 1);

        Assert.Equal(1u, Read(memory, "%MW0"));
        Assert.Equal(1u, Read(memory, "%MW1"));
    }

    [Fact]
    public void TheLinkIsSymmetricSoEitherSideDrivesTheOther()
    {
        var memory = Linked(["%MW0", "%MW1"]);

        memory.WriteScalar(IecAddress.Parse("%MW1"), 0x1234);
        Assert.Equal(0x1234u, Read(memory, "%MW0"));

        memory.WriteScalar(IecAddress.Parse("%MW0"), 0x5678);
        Assert.Equal(0x5678u, Read(memory, "%MW1"));
    }

    [Fact]
    public void MoreThanTwoAddressesCanShareOneValue()
    {
        var memory = Linked(["%MW0", "%MW5", "%MW9"]);

        memory.WriteScalar(IecAddress.Parse("%MW5"), 77);

        Assert.Equal(77u, Read(memory, "%MW0"));
        Assert.Equal(77u, Read(memory, "%MW9"));
    }

    [Fact]
    public void LinksCanCrossMemoryAreas()
    {
        // 입력을 출력에 비추는 것은 흔한 배선 흉내다.
        var memory = Linked(["%IW80", "%QW10"]);

        memory.WriteScalar(IecAddress.Parse("%IW80"), 2500);

        Assert.Equal(2500u, Read(memory, "%QW10"));
    }

    [Fact]
    public void AnUnlinkedNeighbourIsNotTouched()
    {
        var memory = Linked(["%MW0", "%MW1"]);

        memory.WriteScalar(IecAddress.Parse("%MW0"), 9);

        Assert.Equal(0u, Read(memory, "%MW2"));
    }

    // ==================== 비트 묶음 ====================

    [Fact]
    public void BitTenOfOneWordDrivesBitTenOfAnother()
    {
        // 사용자 표기 그대로: MW0 의 10번 비트 ↔ MW1 의 10번 비트.
        var memory = Linked(["%MW0.10", "%MW1.10"]);

        memory.WriteBit(BitNotation.Parse("%MW0.10"), true);

        Assert.True(memory.ReadBit(BitNotation.Parse("%MW1.10")));
        Assert.Equal(1u << 10, Read(memory, "%MW1"));
    }

    [Fact]
    public void TwoBitsInsideTheSameWordCanBeLinked()
    {
        var memory = Linked(["%MW0.10", "%MW0.12"]);

        memory.WriteBit(BitNotation.Parse("%MW0.10"), true);

        Assert.True(memory.ReadBit(BitNotation.Parse("%MW0.12")));
        Assert.Equal((1u << 10) | (1u << 12), Read(memory, "%MW0"));
    }

    [Fact]
    public void ClearingABitClearsItsPartnerToo()
    {
        var memory = Linked(["%MW0.10", "%MW0.12"]);
        memory.WriteBit(BitNotation.Parse("%MW0.10"), true);

        memory.WriteBit(BitNotation.Parse("%MW0.12"), false);

        Assert.False(memory.ReadBit(BitNotation.Parse("%MW0.10")));
        Assert.Equal(0u, Read(memory, "%MW0"));
    }

    [Fact]
    public void WritingANeighbouringBitInTheSameByteDoesNotTriggerTheLink()
    {
        // 바이트 단위로 겹침을 보면 같은 워드의 다른 비트를 구분하지 못한다 — 비트 단위여야 한다.
        var memory = Linked(["%MW0.10", "%MW0.12"]);

        memory.WriteBit(BitNotation.Parse("%MW0.11"), true);

        Assert.False(memory.ReadBit(BitNotation.Parse("%MW0.10")));
        Assert.False(memory.ReadBit(BitNotation.Parse("%MW0.12")));
    }

    [Fact]
    public void ABitLinkSurvivesAWholeWordWrite()
    {
        var memory = Linked(["%MW0.10", "%MW0.12"]);

        // 워드 전체를 쓰면 두 멤버가 함께 덮인다 — 가장 낮은 번지(10번)가 원본이다.
        memory.WriteScalar(IecAddress.Parse("%MW0"), 1u << 10);

        Assert.True(memory.ReadBit(BitNotation.Parse("%MW0.12")));
    }

    [Fact]
    public void AWholeWordWriteThatClearsTheSourceBitClearsThePartner()
    {
        var memory = Linked(["%MW0.10", "%MW0.12"]);
        memory.WriteBit(BitNotation.Parse("%MW0.10"), true);

        memory.WriteScalar(IecAddress.Parse("%MW0"), 1u << 12);

        // 원본(10번)이 0이므로 12번도 0이 된다 — 규칙이 정해져 있어야 결과가 흔들리지 않는다.
        Assert.Equal(0u, Read(memory, "%MW0"));
    }

    // ==================== 아날로그(더블워드·실수 폭) ====================

    [Fact]
    public void DwordLinksCopyAllFourBytes()
    {
        var memory = Linked(["%MD100", "%MD200"]);

        memory.WriteRaw(IecAddress.Parse("%MD100"), [0xEF, 0xBE, 0xAD, 0xDE]);

        Assert.Equal(
            new byte[] { 0xEF, 0xBE, 0xAD, 0xDE },
            memory.ReadRaw(IecAddress.Parse("%MD200")));
    }

    [Fact]
    public void ByteAndLwordWidthsWorkToo()
    {
        var memory = Linked(["%MB40", "%MB41"], ["%ML10", "%ML20"]);

        memory.WriteRaw(IecAddress.Parse("%MB40"), [0x5A]);
        memory.WriteRaw(IecAddress.Parse("%ML10"), [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Equal(new byte[] { 0x5A }, memory.ReadRaw(IecAddress.Parse("%MB41")));
        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            memory.ReadRaw(IecAddress.Parse("%ML20")));
    }

    // ==================== 전파가 폭주하지 않는다 ====================

    [Fact]
    public void PropagationDoesNotLoopForeverWhenGroupsChain()
    {
        // A=B, B=C 처럼 사슬로 엮여도 멈춰야 한다.
        var memory = Linked(["%MW0", "%MW1"], ["%MW1", "%MW2"]);

        memory.WriteScalar(IecAddress.Parse("%MW0"), 5);

        Assert.Equal(5u, Read(memory, "%MW1"));

        // 전파는 한 겹만 돈다 — %MW2 까지 가려면 %MW1 을 직접 써야 한다.
        // 무한 재귀로 멈추지 않는 것이 여기서 확인하려는 바다.
        memory.WriteScalar(IecAddress.Parse("%MW1"), 6);
        Assert.Equal(6u, Read(memory, "%MW2"));
    }

    [Fact]
    public void ContinuousWriteCoveringBothMembersUsesTheLowestAddressAsSource()
    {
        var memory = Linked(["%MW0", "%MW1"]);

        // 바이트 0..3 = %MW0(0x1111) + %MW1(0x2222) 를 한 번에.
        memory.WriteBytes(MemoryArea.M, 0, [0x11, 0x11, 0x22, 0x22]);

        Assert.Equal(0x1111u, Read(memory, "%MW0"));
        Assert.Equal(0x1111u, Read(memory, "%MW1"));
    }

    [Fact]
    public void WriteWordsAlsoPropagates()
    {
        var memory = Linked(["%MW0", "%MW8"]);

        memory.WriteWords(MemoryArea.M, 0, [0xABCD]);

        Assert.Equal(0xABCDu, Read(memory, "%MW8"));
    }

    [Fact]
    public void WithNoLinksNothingChangesAndNothingIsSlowed()
    {
        var memory = new PlcMemory();
        Assert.True(memory.Links.IsEmpty);

        memory.WriteScalar(IecAddress.Parse("%MW0"), 1);

        Assert.Equal(0u, Read(memory, "%MW1"));
    }

    [Fact]
    public void LinksCanBeReplacedAtRuntime()
    {
        var memory = Linked(["%MW0", "%MW1"]);
        memory.WriteScalar(IecAddress.Parse("%MW0"), 3);
        Assert.Equal(3u, Read(memory, "%MW1"));

        memory.Links = MemoryLinks.Empty;
        memory.WriteScalar(IecAddress.Parse("%MW0"), 4);

        Assert.Equal(3u, Read(memory, "%MW1"));
    }

    // ==================== 잘못된 묶음은 만들어지지 않는다 ====================

    [Fact]
    public void AGroupNeedsAtLeastTwoAddresses()
        => Assert.Throws<ArgumentException>(
            () => new ResolvedLinkGroup([IecAddress.Parse("%MW0")]));

    [Fact]
    public void MixedWidthsAreRefusedBecauseThereIsNoSensibleCopy()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ResolvedLinkGroup(
            [IecAddress.Parse("%MW0"), IecAddress.Parse("%MD0")]));

        Assert.Contains("크기가 같아야", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameAddressTwiceIsRefused()
        => Assert.Throws<ArgumentException>(() => new ResolvedLinkGroup(
            [IecAddress.Parse("%MW0"), IecAddress.Parse("%MW0")]));

    [Fact]
    public void TooManyGroupsIsRefusedRatherThanSlowlyStallingTheApp()
    {
        var groups = Enumerable.Range(0, MemoryLinks.MaxGroups + 1)
            .Select(i => new ResolvedLinkGroup(
                [IecAddress.Parse($"%MW{i * 2}"), IecAddress.Parse($"%MW{(i * 2) + 1}")]))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryLinks(groups));
    }

    [Fact]
    public void AGroupCanBeTurnedBackIntoItsSerialisedForm()
    {
        var group = new ResolvedLinkGroup(
            [IecAddress.Parse("%MW0"), IecAddress.Parse("%MW1")], "운전 지령 미러");

        var entry = group.ToEntry();

        Assert.Equal(["%MW0", "%MW1"], entry.Addresses);
        Assert.Equal("운전 지령 미러", entry.Label);
        Assert.Equal("%MW0 = %MW1", group.Text);
    }
}

/// <summary>워드.비트 표기.</summary>
public class BitNotationTests
{
    [Theory]
    [InlineData("%MW0.0", "%MX0")]
    [InlineData("%MW0.10", "%MX10")]
    [InlineData("%MW1.10", "%MX26")]      // 바이트 2 → 비트 16 + 10
    [InlineData("%MW0.15", "%MX15")]
    [InlineData("%MB40.3", "%MX323")]     // 바이트 40 → 비트 320 + 3
    [InlineData("%MD10.31", "%MX351")]    // 바이트 40 → 비트 320 + 31
    [InlineData("%QW10.5", "%QX165")]
    public void WordDotBitBecomesAnAbsoluteBitAddress(string text, string expected)
        => Assert.Equal(expected, BitNotation.Parse(text).Text);

    [Theory]
    [InlineData("%MW320")]
    [InlineData("%MX801")]
    [InlineData("%MD422")]
    public void APlainAddressPassesThroughUnchanged(string text)
        => Assert.Equal(text, BitNotation.Parse(text).Text);

    [Theory]
    [InlineData("%MW0.16")]    // 워드는 비트 0..15
    [InlineData("%MW0.-1")]
    [InlineData("%MB0.8")]
    public void ABitNumberOutsideTheWidthIsRefused(string text)
        => Assert.False(BitNotation.TryParse(text, out _, out _));

    [Theory]
    [InlineData("%MX10.1")]    // 비트에 다시 비트를 붙일 수 없다
    [InlineData("%MW0.abc")]
    [InlineData("MW0.1")]
    [InlineData("")]
    [InlineData(null)]
    public void GarbageIsRefusedWithAReason(string? text)
    {
        Assert.False(BitNotation.TryParse(text, out var address, out var error));
        Assert.Null(address);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseThrowsWithTheSameReasonTryParseReports()
    {
        Assert.Throws<FormatException>(() => BitNotation.Parse("MW0.1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitNotation.Parse("%MW0.16"));
    }

    [Theory]
    [InlineData("%MW0.10", true)]
    [InlineData("%MW0", false)]
    public void TheDottedFormIsRecognisable(string text, bool expected)
        => Assert.Equal(expected, BitNotation.LooksLikeBitOfWord(text));
}
