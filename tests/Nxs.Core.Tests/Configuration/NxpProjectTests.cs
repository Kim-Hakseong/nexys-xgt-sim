using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Configuration;

/// <summary>PRD X-08 — 프로젝트 파일(.nxp JSON) 저장/로드 라운드트립.</summary>
public class NxpProjectTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nxsim-nxp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static NxpProject SampleProject() => new()
    {
        Io = IoConfiguration.CreateDefaultRack(),
        Server = new ServerSettings { BindAddress = "192.168.0.50", Port = 2004 },
        InitialValues =
        [
            new InitialValue { Address = "%MW100", Value = 0x1234 },
            new InitialValue { Address = "%IX512", Value = 1 },
        ],
        AnalogChannels =
        [
            new AnalogChannelSettings
            {
                SlotNumber = 5,
                Channel = 0,
                Scale = new AnalogChannelScale
                {
                    RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
                },
            },
        ],
    };

    [Fact]
    public void SaveThenLoadReturnsAnEquivalentProject()
    {
        var path = PathFor("rack.nxp");
        var original = SampleProject();

        NxpProjectFile.Save(path, original);
        var loaded = NxpProjectFile.Load(path);

        Assert.Equal(original.Server.BindAddress, loaded.Server.BindAddress);
        Assert.Equal(original.Server.Port, loaded.Server.Port);
        Assert.Equal(original.Io.Addressing.SlotPoints, loaded.Io.Addressing.SlotPoints);
        Assert.Equal(original.Io.Addressing.SlotsPerBase, loaded.Io.Addressing.SlotsPerBase);
        Assert.Equal(
            original.Io.Bases.Single().Slots.Select(s => s.Module?.ProductName),
            loaded.Io.Bases.Single().Slots.Select(s => s.Module?.ProductName));
        Assert.Equal(original.InitialValues.Count, loaded.InitialValues.Count);
        Assert.Equal("%MW100", loaded.InitialValues[0].Address);
        Assert.Equal(0x1234u, loaded.InitialValues[0].Value);
        Assert.Equal(10, loaded.AnalogChannels.Single().Scale.EngineeringMax);
        Assert.Equal("V", loaded.AnalogChannels.Single().Scale.Unit);
    }

    [Fact]
    public void LoadedConfigurationProducesTheSameMemoryMap()
    {
        var path = PathFor("map.nxp");
        NxpProjectFile.Save(path, SampleProject());

        var expected = IoConfiguration.CreateDefaultRack().BuildMap();
        var actual = NxpProjectFile.Load(path).Io.BuildMap();

        Assert.Equal(
            expected.Select(m => (m.SlotNumber, m.Area, m.StartBit, m.BitLength)),
            actual.Select(m => (m.SlotNumber, m.Area, m.StartBit, m.BitLength)));
    }

    [Fact]
    public void SavedFileIsIndentedJsonWithReadableKeys()
    {
        var path = PathFor("readable.nxp");
        NxpProjectFile.Save(path, SampleProject());

        var text = File.ReadAllText(path);

        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.Contains("formatVersion", text, StringComparison.Ordinal);
        Assert.Contains("XGI-D24A", text, StringComparison.Ordinal);
        Assert.Contains("192.168.0.50", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureFormatVersionIsRejectedWithAClearMessage()
    {
        var path = PathFor("future.nxp");
        File.WriteAllText(path, $"{{\"formatVersion\": {NxpProject.CurrentFormatVersion + 1}}}");

        var ex = Assert.Throws<NxpFormatException>(() => NxpProjectFile.Load(path));
        Assert.Contains("formatVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJsonIsRejectedAsNxpFormatException()
    {
        var path = PathFor("broken.nxp");
        File.WriteAllText(path, "{ this is not json ");

        Assert.Throws<NxpFormatException>(() => NxpProjectFile.Load(path));
    }

    [Fact]
    public void SaveIsAtomicSoAFailedSaveLeavesThePreviousFileIntact()
    {
        var path = PathFor("atomic.nxp");
        NxpProjectFile.Save(path, SampleProject());
        var before = File.ReadAllText(path);

        // 직렬화 불가한 구성(포트 범위 위반)으로 저장 시도 → 검증 단계에서 실패해야 한다.
        var invalid = SampleProject() with { Server = new ServerSettings { BindAddress = "x", Port = 99999 } };
        Assert.ThrowsAny<Exception>(() => NxpProjectFile.Save(path, invalid));

        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void ApplyInitialValuesWritesThemIntoMemory()
    {
        var project = SampleProject();
        var memory = new PlcMemory(new PlcMemoryOptions { Addressing = project.Io.Addressing });

        project.ApplyInitialValues(memory);

        Assert.Equal(0x1234u, memory.ReadScalar(IecAddress.Parse("%MW100")));
        Assert.True(memory.ReadBit(IecAddress.Parse("%IX512")));
    }

    [Fact]
    public void ApplyInitialValuesRejectsAnUnparsableAddress()
    {
        var project = SampleProject() with
        {
            InitialValues = [new InitialValue { Address = "%ZW1", Value = 1 }],
        };
        var memory = new PlcMemory();

        Assert.Throws<FormatException>(() => project.ApplyInitialValues(memory));
    }

    [Fact]
    public void DefaultProjectIsSaveableAndLoadable()
    {
        var path = PathFor("default.nxp");

        NxpProjectFile.Save(path, NxpProject.CreateDefault(port: 2004));
        var loaded = NxpProjectFile.Load(path);

        Assert.Equal(2004, loaded.Server.Port);
        Assert.Equal(NxpProject.CurrentFormatVersion, loaded.FormatVersion);
        Assert.NotEmpty(loaded.Io.BuildMap());
    }

    [Fact]
    public void ServerSettingsConvertToServerOptions()
    {
        var options = new ServerSettings { BindAddress = "127.0.0.1", Port = 2004 }.ToServerOptions();

        Assert.Equal(2004, options.Port);
        Assert.Equal("127.0.0.1", options.BindAddress.ToString());
    }

    [Fact]
    public void ServerSettingsWithAnyAddressMeansAllNics()
    {
        var options = new ServerSettings { BindAddress = "0.0.0.0", Port = 2004 }.ToServerOptions();

        Assert.Equal(System.Net.IPAddress.Any, options.BindAddress);
    }

    [Fact]
    public void ServerSettingsRejectsAnUnparsableBindAddress()
    {
        Assert.Throws<FormatException>(
            () => new ServerSettings { BindAddress = "not-an-ip", Port = 2004 }.ToServerOptions());
    }
}
