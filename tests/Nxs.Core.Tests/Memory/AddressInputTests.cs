using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Memory;

/// <summary>
/// 손 입력 주소 정규화 — 한글 IME 전각 문자가 가장 흔한 실패 원인이다.
/// </summary>
public class AddressInputTests
{
    [Theory]
    [InlineData("%MW0", "%MW0")]
    [InlineData("%MW000", "%MW000")]
    [InlineData("  %MW320  ", "%MW320")]
    [InlineData("%mw320", "%MW320")]
    [InlineData("MW320", "%MW320")]          // 선행 % 보충
    [InlineData("mw320", "%MW320")]
    [InlineData("% M W 3 2 0", "%MW320")]    // 내부 공백 제거
    public void NormalizesOrdinaryInput(string input, string expected)
        => Assert.Equal(expected, AddressInput.Normalize(input));

    [Fact]
    public void FoldsFullWidthCharactersFromKoreanIme()
    {
        // 한글 IME 전각 모드: ％ＭＷ３２０
        const string fullWidth = "％ＭＷ３２０";

        Assert.Equal("%MW320", AddressInput.Normalize(fullWidth));
    }

    [Fact]
    public void FullWidthInputBecomesParsableAfterNormalizing()
    {
        const string fullWidth = "％ＭＷ０";   // ％ＭＷ０

        Assert.False(IecAddress.TryParse(fullWidth, out _));
        Assert.True(IecAddress.TryParse(AddressInput.Normalize(fullWidth), out var address));
        Assert.Equal(0, address.Offset);
        Assert.Equal(DataSize.Word, address.Size);
    }

    [Fact]
    public void FullWidthPercentAloneIsFolded()
        => Assert.Equal("%MW1", AddressInput.Normalize("％MW1"));

    [Fact]
    public void FullWidthDigitsAreFolded()
        => Assert.Equal("%MD422", AddressInput.Normalize("%MD４２２"));

    [Fact]
    public void FullWidthSpaceIsRemoved()
        => Assert.Equal("%MW320", AddressInput.Normalize("%MW　320"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　")]
    public void EmptyInputStaysEmpty(string? input)
        => Assert.Equal(string.Empty, AddressInput.Normalize(input));

    [Fact]
    public void NormalizingDoesNotInventAValidAddressFromGarbage()
    {
        Assert.False(IecAddress.TryParse(AddressInput.Normalize("헛소리"), out _));
        Assert.False(IecAddress.TryParse(AddressInput.Normalize("%ZW10"), out _));
    }

    [Fact]
    public void DescribeShowsCodePointsSoInvisibleCharactersAreVisible()
    {
        var description = AddressInput.Describe("％MW0");

        Assert.Contains("U+FF05", description, StringComparison.Ordinal);
        Assert.Contains("U+004D", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeHandlesEmpty()
        => Assert.Equal("빈 입력", AddressInput.Describe(""));
}
