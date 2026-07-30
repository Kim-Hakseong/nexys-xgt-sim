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
    [InlineData("%ML50")]
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
                new WatchEntry
                {
                    Address = "%MD500", Label = "유량 (실수)",
                    Format = WatchFormat.Float, Order = ByteOrder.Abcd,
                },
                new WatchEntry { Address = "%ML60", Label = "적산 (배정도)", Format = WatchFormat.Double },
            ],
        };

        NxpProjectFile.Save(path, project);
        var loaded = NxpProjectFile.Load(path);

        Assert.Equal(5, loaded.Watches.Count);
        Assert.Equal("%MW320", loaded.Watches[0].Address);
        Assert.Equal("설정 압력", loaded.Watches[0].Label);
        Assert.Equal(WatchFormat.Hex, loaded.Watches[1].Format);
        Assert.Equal(WatchFormat.Bool, loaded.Watches[2].Format);
        Assert.Equal(WatchFormat.Float, loaded.Watches[3].Format);
        Assert.Equal(ByteOrder.Abcd, loaded.Watches[3].Order);
        Assert.Equal(WatchFormat.Double, loaded.Watches[4].Format);
        Assert.Equal(ByteOrder.Dcba, loaded.Watches[4].Order);
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
    public void WatchRoundTripsThroughMemory()
    {
        var memory = new PlcMemory();
        var entry = new WatchEntry { Address = "%MD422", Format = WatchFormat.Hex };
        var address = entry.Resolve();

        memory.WriteScalar(address, 0xDEADBEEF);

        Assert.Equal("0xDEADBEEF",
            WatchValue.Render(memory.ReadRaw(address), entry.Format, entry.Order));
    }

    [Fact]
    public void LongWordWatchRoundTripsAsDouble()
    {
        var memory = new PlcMemory();
        var entry = new WatchEntry { Address = "%ML50", Format = WatchFormat.Double };
        var address = entry.Resolve();

        var bytes = WatchValue.Parse("2.718281828459045", 8, entry.Format, entry.Order);
        memory.WriteRaw(address, bytes!);

        Assert.Equal("2.718281828459045",
            WatchValue.Render(memory.ReadRaw(address), entry.Format, entry.Order));
    }

    [Fact]
    public void ByteOrderIsPersistedPerEntry()
    {
        var entry = new WatchEntry { Address = "%MD422", Format = WatchFormat.Float, Order = ByteOrder.Abcd };

        Assert.Equal(ByteOrder.Abcd, entry.Order);
        Assert.Equal(ByteOrder.Dcba, new WatchEntry { Address = "%MW0" }.Order);
    }
}
