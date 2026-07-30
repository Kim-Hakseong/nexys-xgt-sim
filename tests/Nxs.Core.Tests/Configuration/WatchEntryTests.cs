using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Configuration;

/// <summary>
/// 사용자 지정 주소 워치 목록 — LabVIEW 는 대부분 %M 영역과 대화하므로 랙 매핑 밖의
/// 임의 주소(%MW320, %MD422 …)를 직접 보고 쓸 수 있어야 한다.
/// </summary>
public class WatchEntryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nxsim-watch-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void EntryResolvesItsAddress()
    {
        var entry = new WatchEntry { Address = "%MW320", Label = "설정 압력" };

        var resolved = entry.Resolve();

        Assert.Equal(MemoryArea.M, resolved.Area);
        Assert.Equal(DataSize.Word, resolved.Size);
        Assert.Equal(320, resolved.Offset);
    }

    [Fact]
    public void DWordEntryResolves()
    {
        var resolved = new WatchEntry { Address = "%MD422" }.Resolve();

        Assert.Equal(DataSize.DWord, resolved.Size);
        Assert.Equal(422, resolved.Offset);
        Assert.Equal(1688, resolved.ByteStart);
    }

    [Theory]
    [InlineData("%MW320")]
    [InlineData("%MD422")]
    [InlineData("%MX801")]
    [InlineData("%MB40")]
    [InlineData("%IW80")]
    [InlineData("%QX1024")]
    [InlineData("%IX0.2.5")]
    public void EveryAddressFormTheParserAcceptsIsAValidWatchTarget(string address)
        => Assert.True(WatchEntry.IsValid(address));

    [Theory]
    [InlineData("%ZW10")]
    [InlineData("%IX0.2")]
    [InlineData("MW320")]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidAddressIsRejectedBeforeItReachesTheList(string address)
        => Assert.False(WatchEntry.IsValid(address));

    [Fact]
    public void IsValidRejectsNull()
        => Assert.False(WatchEntry.IsValid(null));

    [Fact]
    public void ResolvingAnInvalidAddressThrowsFormatException()
        => Assert.Throws<FormatException>(() => new WatchEntry { Address = "%ZW1" }.Resolve());

    [Fact]
    public void EntriesSurviveNxpSaveAndLoad()
    {
        var path = Path.Combine(_dir, "watch.nxp");
        var project = NxpProject.CreateDefault(port: 2004) with
        {
            Watches =
            [
                new WatchEntry { Address = "%MW320", Label = "설정 압력", Format = WatchFormat.Decimal },
                new WatchEntry { Address = "%MD422", Label = "적산 유량", Format = WatchFormat.Hex },
                new WatchEntry { Address = "%MX801", Label = "운전 지령", Format = WatchFormat.Bool },
            ],
        };

        NxpProjectFile.Save(path, project);
        var loaded = NxpProjectFile.Load(path);

        Assert.Equal(3, loaded.Watches.Count);
        Assert.Equal("%MW320", loaded.Watches[0].Address);
        Assert.Equal("설정 압력", loaded.Watches[0].Label);
        Assert.Equal(WatchFormat.Hex, loaded.Watches[1].Format);
        Assert.Equal(WatchFormat.Bool, loaded.Watches[2].Format);
    }

    [Fact]
    public void ProjectWithoutWatchesLoadsAsEmpty()
        => Assert.Empty(NxpProject.CreateDefault(port: 2004).Watches);

    [Fact]
    public void SaveRejectsAProjectWithAnUnparsableWatchAddress()
    {
        var path = Path.Combine(_dir, "bad.nxp");
        var project = NxpProject.CreateDefault(port: 2004) with
        {
            Watches = [new WatchEntry { Address = "%ZW1" }],
        };

        Assert.Throws<FormatException>(() => NxpProjectFile.Save(path, project));
    }

    [Fact]
    public void FormatterRendersDecimalHexBinaryAndBool()
    {
        Assert.Equal("4660", WatchEntry.Render(0x1234, DataSize.Word, WatchFormat.Decimal));
        Assert.Equal("0x1234", WatchEntry.Render(0x1234, DataSize.Word, WatchFormat.Hex));
        Assert.Equal("0001 0010 0011 0100", WatchEntry.Render(0x1234, DataSize.Word, WatchFormat.Binary));
        Assert.Equal("ON", WatchEntry.Render(1, DataSize.Bit, WatchFormat.Bool));
        Assert.Equal("OFF", WatchEntry.Render(0, DataSize.Bit, WatchFormat.Bool));
    }

    [Fact]
    public void SignedFormatShowsNegativeValuesForWordAndDWord()
    {
        Assert.Equal("-1", WatchEntry.Render(0xFFFF, DataSize.Word, WatchFormat.Signed));
        Assert.Equal("-2", WatchEntry.Render(0xFFFFFFFE, DataSize.DWord, WatchFormat.Signed));
        Assert.Equal("32767", WatchEntry.Render(0x7FFF, DataSize.Word, WatchFormat.Signed));
    }

    [Fact]
    public void HexWidthMatchesTheDataSize()
    {
        Assert.Equal("0xAB", WatchEntry.Render(0xAB, DataSize.Byte, WatchFormat.Hex));
        Assert.Equal("0x1234", WatchEntry.Render(0x1234, DataSize.Word, WatchFormat.Hex));
        Assert.Equal("0x12345678", WatchEntry.Render(0x12345678, DataSize.DWord, WatchFormat.Hex));
    }

    [Fact]
    public void ParseInputAcceptsDecimalHexAndBool()
    {
        Assert.Equal(4660u, WatchEntry.ParseInput("4660", DataSize.Word));
        Assert.Equal(4660u, WatchEntry.ParseInput("0x1234", DataSize.Word));
        Assert.Equal(4660u, WatchEntry.ParseInput("0X1234", DataSize.Word));
        Assert.Equal(1u, WatchEntry.ParseInput("ON", DataSize.Bit));
        Assert.Equal(0u, WatchEntry.ParseInput("off", DataSize.Bit));
        Assert.Equal(1u, WatchEntry.ParseInput("true", DataSize.Bit));
    }

    [Fact]
    public void ParseInputAcceptsNegativeValuesForSignedEntry()
    {
        Assert.Equal(0xFFFFu, WatchEntry.ParseInput("-1", DataSize.Word));
        Assert.Equal(0xFFFFFFFEu, WatchEntry.ParseInput("-2", DataSize.DWord));
    }

    [Fact]
    public void ParseInputRejectsGarbageAndOutOfRange()
    {
        Assert.Null(WatchEntry.ParseInput("헛소리", DataSize.Word));
        Assert.Null(WatchEntry.ParseInput("", DataSize.Word));
        Assert.Null(WatchEntry.ParseInput("70000", DataSize.Word));
        Assert.Null(WatchEntry.ParseInput("256", DataSize.Byte));
        Assert.Null(WatchEntry.ParseInput("-40000", DataSize.Word));
    }

    [Fact]
    public void WatchRoundTripsThroughMemory()
    {
        var memory = new PlcMemory();
        var entry = new WatchEntry { Address = "%MD422" };
        var address = entry.Resolve();

        memory.WriteScalar(address, 0xDEADBEEF);

        Assert.Equal("0xDEADBEEF", WatchEntry.Render(
            memory.ReadScalar(address), address.Size, WatchFormat.Hex));
    }
}
