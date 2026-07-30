using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Configuration;

/// <summary>
/// 사용자 지정 디지털 점 — 임의 주소를 비트 배열로 펼친다.
/// </summary>
public class DigitalPointEntryTests
{
    [Theory]
    [InlineData("%MX801", 1)]
    [InlineData("%MB40", 8)]
    [InlineData("%MW320", 16)]
    [InlineData("%MD422", 32)]
    [InlineData("%ML50", 64)]
    [InlineData("%QX2000", 1)]
    [InlineData("%IW80", 16)]
    public void BitCountFollowsTheAddressWidth(string address, int expected)
        => Assert.Equal(expected, DigitalPointEntry.BitCountOf(IecAddress.Parse(address)));

    [Theory]
    [InlineData("%MX801")]
    [InlineData("%MB40")]
    [InlineData("%MW320")]
    [InlineData("%MD422")]
    [InlineData("%ML50")]
    [InlineData("%QX2000")]
    [InlineData("%IX0.2.5")]
    public void EveryAddressWidthIsNowAccepted(string address)
        => Assert.True(DigitalPointEntry.IsValid(address));

    [Theory]
    [InlineData("%ZW10")]
    [InlineData("%IX0.2")]
    [InlineData("MW320")]
    [InlineData("")]
    public void InvalidAddressIsStillRejected(string address)
        => Assert.False(DigitalPointEntry.IsValid(address));

    [Fact]
    public void BitZeroOfAWordIsTheLowestBitOfItsStartByte()
    {
        // %MW0 비트0 = %MX0 (M1 골든 벡터: %MW0=0x0001 → %MX0=true)
        var word = IecAddress.Parse("%MW0");

        Assert.Equal("%MX0", DigitalPointEntry.BitAddressOf(word, 0).Text);
        Assert.Equal("%MX15", DigitalPointEntry.BitAddressOf(word, 15).Text);
    }

    [Fact]
    public void WordAtOffsetExpandsToTheCorrectAbsoluteBits()
    {
        // %MW320 은 바이트 640 부터 → 비트 5120..5135
        var word = IecAddress.Parse("%MW320");

        Assert.Equal("%MX5120", DigitalPointEntry.BitAddressOf(word, 0).Text);
        Assert.Equal("%MX5135", DigitalPointEntry.BitAddressOf(word, 15).Text);
    }

    [Fact]
    public void DWordExpandsToThirtyTwoBits()
    {
        var dword = IecAddress.Parse("%MD10");   // 바이트 40 → 비트 320..351

        Assert.Equal("%MX320", DigitalPointEntry.BitAddressOf(dword, 0).Text);
        Assert.Equal("%MX351", DigitalPointEntry.BitAddressOf(dword, 31).Text);
    }

    [Fact]
    public void BitAddressExpandsToItselfOnly()
    {
        var bit = IecAddress.Parse("%MX801");

        Assert.Equal(1, DigitalPointEntry.BitCountOf(bit));
        Assert.Equal("%MX801", DigitalPointEntry.BitAddressOf(bit, 0).Text);
    }

    [Fact]
    public void AreaIsPreservedWhenExpanding()
    {
        Assert.Equal("%QX16", DigitalPointEntry.BitAddressOf(IecAddress.Parse("%QW1"), 0).Text);
        Assert.Equal("%IX16", DigitalPointEntry.BitAddressOf(IecAddress.Parse("%IW1"), 0).Text);
    }

    [Fact]
    public void BitIndexOutOfRangeIsRejected()
    {
        var word = IecAddress.Parse("%MW320");

        Assert.Throws<ArgumentOutOfRangeException>(() => DigitalPointEntry.BitAddressOf(word, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => DigitalPointEntry.BitAddressOf(word, -1));
    }

    [Fact]
    public void ExpandedBitsMatchTheWordValueInMemory()
    {
        // 워드에 값을 쓰고 펼친 비트로 읽으면 리틀엔디안 비트 배치와 일치해야 한다.
        var memory = new PlcMemory();
        var word = IecAddress.Parse("%MW320");
        memory.WriteScalar(word, 0b0000_0000_0000_1011);   // 비트 0,1,3

        Assert.True(memory.ReadBit(DigitalPointEntry.BitAddressOf(word, 0)));
        Assert.True(memory.ReadBit(DigitalPointEntry.BitAddressOf(word, 1)));
        Assert.False(memory.ReadBit(DigitalPointEntry.BitAddressOf(word, 2)));
        Assert.True(memory.ReadBit(DigitalPointEntry.BitAddressOf(word, 3)));
        Assert.False(memory.ReadBit(DigitalPointEntry.BitAddressOf(word, 15)));
    }

    [Fact]
    public void TogglingExpandedBitsBuildsTheWordValue()
    {
        var memory = new PlcMemory();
        var word = IecAddress.Parse("%MW320");

        memory.WriteBit(DigitalPointEntry.BitAddressOf(word, 8), true);
        memory.WriteBit(DigitalPointEntry.BitAddressOf(word, 15), true);

        Assert.Equal(0b1000_0001_0000_0000u, memory.ReadScalar(word));
    }

    [Fact]
    public void EntriesWithEveryWidthSurviveNxpSaveAndLoad()
    {
        var dir = Directory.CreateTempSubdirectory("nxsim-dpe-").FullName;
        try
        {
            var path = Path.Combine(dir, "points.nxp");
            var project = NxpProject.CreateDefault(port: 2004) with
            {
                DigitalPoints =
                [
                    new DigitalPointEntry { Address = "%MX801", Label = "비트" },
                    new DigitalPointEntry { Address = "%MW320", Label = "워드" },
                    new DigitalPointEntry { Address = "%MD422", Label = "더블워드" },
                ],
            };

            NxpProjectFile.Save(path, project);
            var loaded = NxpProjectFile.Load(path);

            Assert.Equal(3, loaded.DigitalPoints.Count);
            Assert.Equal("%MW320", loaded.DigitalPoints[1].Address);
            Assert.Equal("더블워드", loaded.DigitalPoints[2].Label);
            Assert.Equal(32, DigitalPointEntry.BitCountOf(loaded.DigitalPoints[2].Resolve()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
