using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Configuration;

/// <summary>
/// PRD X-02 — I/O 구성 모델 → 메모리 자동 매핑.
/// 대상 랙은 CONTEXT.md 기재 구성(XG5000 I/O 파라미터 기준).
/// </summary>
public class IoConfigurationTests
{
    [Fact]
    public void CatalogDescribesTheContextRackModules()
    {
        Assert.Equal(32, ModuleCatalog.XgiD24A.PointCount);
        Assert.Equal(ModuleKind.DigitalInput, ModuleCatalog.XgiD24A.Kind);

        Assert.Equal(32, ModuleCatalog.XgqTr4A.PointCount);
        Assert.Equal(ModuleKind.DigitalOutput, ModuleCatalog.XgqTr4A.Kind);

        Assert.Equal(16, ModuleCatalog.XgfAd16A.ChannelCount);
        Assert.Equal(ModuleKind.AnalogInput, ModuleCatalog.XgfAd16A.Kind);

        Assert.Equal(ModuleKind.Communication, ModuleCatalog.XglEfmtB.Kind);
        Assert.Equal(ModuleKind.Communication, ModuleCatalog.XglC42A.Kind);
    }

    [Fact]
    public void DefaultRackMatchesTheContextSlotLayout()
    {
        var config = IoConfiguration.CreateDefaultRack();
        var slots = config.Bases.Single().Slots.OrderBy(s => s.SlotNumber).ToArray();

        Assert.Equal(7, slots.Length);
        Assert.Equal("XGL-EFMT(B)", slots[0].Module!.ProductName);
        Assert.Equal("XGL-C42A", slots[1].Module!.ProductName);
        Assert.Equal("XGI-D24A", slots[2].Module!.ProductName);
        Assert.Equal("XGI-D24A", slots[3].Module!.ProductName);
        Assert.Equal("XGQ-TR4A", slots[4].Module!.ProductName);
        Assert.Equal("XGF-AD16A", slots[5].Module!.ProductName);
        Assert.Equal("XGF-AD16A", slots[6].Module!.ProductName);
    }

    [Fact]
    public void DigitalInputSlotsMapToInputAreaAtSlotStride()
    {
        var config = IoConfiguration.CreateDefaultRack();
        var map = config.BuildMap();

        var slot2 = map.Single(m => m.SlotNumber == 2);
        Assert.Equal(MemoryArea.I, slot2.Area);
        Assert.Equal(2 * 256, slot2.StartBit);
        Assert.Equal(32, slot2.BitLength);
        Assert.Equal("%IX512", slot2.StartAddressText);

        var slot3 = map.Single(m => m.SlotNumber == 3);
        Assert.Equal(MemoryArea.I, slot3.Area);
        Assert.Equal(3 * 256, slot3.StartBit);
        Assert.Equal(32, slot3.BitLength);
    }

    [Fact]
    public void DigitalOutputSlotMapsToOutputArea()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();

        var slot4 = map.Single(m => m.SlotNumber == 4);
        Assert.Equal(MemoryArea.Q, slot4.Area);
        Assert.Equal(4 * 256, slot4.StartBit);
        Assert.Equal(32, slot4.BitLength);
        Assert.Equal("%QX1024", slot4.StartAddressText);
    }

    [Fact]
    public void AnalogInputSlotsMapToInputWordsOneWordPerChannel()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();

        var slot5 = map.Single(m => m.SlotNumber == 5);
        Assert.Equal(MemoryArea.I, slot5.Area);
        Assert.Equal((5 * 256) / 16, slot5.StartWord);
        Assert.Equal(80, slot5.StartWord);
        Assert.Equal(16, slot5.WordLength);
        Assert.Equal("%IW80", slot5.StartAddressText);

        var slot6 = map.Single(m => m.SlotNumber == 6);
        Assert.Equal(96, slot6.StartWord);
        Assert.Equal(16, slot6.WordLength);
    }

    [Fact]
    public void CommunicationModulesOccupyNoProcessDataAndAreNotMapped()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();

        Assert.DoesNotContain(map, m => m.SlotNumber is 0 or 1);
    }

    [Fact]
    public void MappedRangesDoNotOverlapWithinAnArea()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();

        foreach (var area in new[] { MemoryArea.I, MemoryArea.Q })
        {
            var ranges = map.Where(m => m.Area == area)
                .OrderBy(m => m.StartBit)
                .ToArray();
            for (var i = 1; i < ranges.Length; i++)
            {
                Assert.True(
                    ranges[i].StartBit >= ranges[i - 1].StartBit + ranges[i - 1].BitLength,
                    $"슬롯 {ranges[i - 1].SlotNumber}과 {ranges[i].SlotNumber}의 범위가 겹칩니다");
            }
        }
    }

    [Fact]
    public void ChannelAddressResolvesPerAnalogChannel()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();
        var slot5 = map.Single(m => m.SlotNumber == 5);

        Assert.Equal("%IW80", slot5.ChannelAddress(0).Text);
        Assert.Equal("%IW95", slot5.ChannelAddress(15).Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => slot5.ChannelAddress(16));
    }

    [Fact]
    public void PointAddressResolvesPerDigitalPoint()
    {
        var map = IoConfiguration.CreateDefaultRack().BuildMap();
        var slot2 = map.Single(m => m.SlotNumber == 2);

        Assert.Equal("%IX512", slot2.PointAddress(0).Text);
        Assert.Equal("%IX543", slot2.PointAddress(31).Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => slot2.PointAddress(32));
    }

    [Fact]
    public void ModuleLargerThanSlotStrideIsRejected()
    {
        // AD16A 는 16워드(256비트)를 쓴다 — 스트라이드 64점으로는 담을 수 없다.
        var config = new IoConfiguration
        {
            Addressing = new AddressingOptions { SlotPoints = 64, SlotsPerBase = 12 },
            Bases =
            [
                new BaseConfig
                {
                    BaseNumber = 0,
                    Slots = [new SlotConfig { SlotNumber = 0, Module = ModuleCatalog.XgfAd16A }],
                },
            ],
        };

        var ex = Assert.Throws<IoConfigurationException>(() => config.BuildMap());
        Assert.Contains("64", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSlotNumberIsRejected()
    {
        var config = new IoConfiguration
        {
            Bases =
            [
                new BaseConfig
                {
                    BaseNumber = 0,
                    Slots =
                    [
                        new SlotConfig { SlotNumber = 2, Module = ModuleCatalog.XgiD24A },
                        new SlotConfig { SlotNumber = 2, Module = ModuleCatalog.XgiD24A },
                    ],
                },
            ],
        };

        Assert.Throws<IoConfigurationException>(() => config.BuildMap());
    }

    [Fact]
    public void SlotNumberBeyondBaseCapacityIsRejected()
    {
        var config = new IoConfiguration
        {
            Addressing = new AddressingOptions { SlotPoints = 256, SlotsPerBase = 4 },
            Bases =
            [
                new BaseConfig
                {
                    BaseNumber = 0,
                    Slots = [new SlotConfig { SlotNumber = 9, Module = ModuleCatalog.XgiD24A }],
                },
            ],
        };

        Assert.Throws<IoConfigurationException>(() => config.BuildMap());
    }

    [Fact]
    public void EmptySlotIsAllowedAndUnmapped()
    {
        var config = new IoConfiguration
        {
            Bases =
            [
                new BaseConfig
                {
                    BaseNumber = 0,
                    Slots =
                    [
                        new SlotConfig { SlotNumber = 0, Module = null },
                        new SlotConfig { SlotNumber = 1, Module = ModuleCatalog.XgiD24A },
                    ],
                },
            ],
        };

        var map = config.BuildMap();

        Assert.Equal(1, map.Single().SlotNumber);
    }

    [Fact]
    public void MapFitsWithinDefaultMemoryAreaSize()
    {
        var config = IoConfiguration.CreateDefaultRack();
        var map = config.BuildMap();
        var memory = new PlcMemory(new PlcMemoryOptions { Addressing = config.Addressing });

        foreach (var m in map)
        {
            Assert.True(
                (m.StartBit + m.BitLength) / 8 <= memory.AreaSizeBytes,
                $"슬롯 {m.SlotNumber} 범위가 영역 크기를 넘습니다");
        }
    }
}
