using Nxs.Core.Protocol;

namespace Nxs.Core.Tests.Protocol;

public class HexTests
{
    [Fact]
    public void FormatsBytesAsUppercaseSpaceSeparatedPairs()
    {
        Assert.Equal("00 0F A5 FF", Hex.Format(new byte[] { 0x00, 0x0F, 0xA5, 0xFF }));
    }

    [Fact]
    public void FormatsEmptyAsEmptyString()
    {
        Assert.Equal(string.Empty, Hex.Format(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ParsesSpaceSeparatedHex()
    {
        Assert.Equal(new byte[] { 0x00, 0x0F, 0xA5, 0xFF }, Hex.Parse("00 0F A5 FF"));
    }

    [Fact]
    public void ParseIgnoresWhitespaceVariationsAndCase()
    {
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, Hex.Parse("de ad\tBE\r\nef"));
    }

    [Fact]
    public void ParseAcceptsUnseparatedHex()
    {
        Assert.Equal(new byte[] { 0xDE, 0xAD }, Hex.Parse("DEAD"));
    }

    [Fact]
    public void ParseRejectsOddDigitCount()
    {
        Assert.Throws<FormatException>(() => Hex.Parse("ABC"));
    }

    [Fact]
    public void ParseRejectsNonHexCharacters()
    {
        Assert.Throws<FormatException>(() => Hex.Parse("AB ZZ"));
    }

    [Fact]
    public void FormatAndParseRoundTripOverAllByteValues()
    {
        var all = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            all[i] = (byte)i;
        }

        Assert.Equal(all, Hex.Parse(Hex.Format(all)));
    }
}
