using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Simulator;
using Nxs.TestKit;

namespace Nxs.Integration.Tests;

/// <summary>
/// 시뮬레이터 엔진 — 메모리 + 구성 + 서버를 묶는 UI 무관 계층.
/// 코덱은 주입된다: XGT 코덱이 없으면(⛔ M2) 서버를 켤 수 없고 그 사실을 명시해야 한다.
/// </summary>
public class SimulatorEngineTests
{
    private static NxpProject Project() => new()
    {
        Io = IoConfiguration.CreateDefaultRack(),
        Server = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 },
        InitialValues = [new InitialValue { Address = "%MW100", Value = 0x0042 }],
    };

    private static SimulatorEngine NewEngine(bool withCodec = true)
        => new(Project(), withCodec ? m => new TestOnlyFrameCodec(new PlcRequestExecutor(m)) : null);

    [Fact]
    public void EngineAppliesInitialValuesFromTheProject()
    {
        using var engine = NewEngine();

        Assert.Equal(0x0042u, engine.Memory.ReadScalar(IecAddress.Parse("%MW100")));
    }

    [Fact]
    public void EngineExposesTheConfiguredModuleMap()
    {
        using var engine = NewEngine();

        Assert.Equal(5, engine.Map.Count);
        Assert.Contains(engine.Map, m => m.SlotNumber == 2 && m.StartBit == 512);
    }

    [Fact]
    public void EngineWithoutCodecReportsServerUnavailableWithAReason()
    {
        using var engine = NewEngine(withCodec: false);

        Assert.False(engine.CanStartServer);
        Assert.NotNull(engine.ServerUnavailableReason);
        Assert.Contains("spec", engine.ServerUnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartingServerWithoutCodecThrowsInsteadOfPretending()
    {
        using var engine = NewEngine(withCodec: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartServerAsync());
        Assert.Contains("spec", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EngineWithCodecCanStartServer()
    {
        using var engine = NewEngine();

        Assert.True(engine.CanStartServer);
        Assert.Null(engine.ServerUnavailableReason);
    }

    [Fact]
    public async Task StartServerBindsAndReportsEndpoint()
    {
        using var engine = NewEngine();

        await engine.StartServerAsync();

        Assert.True(engine.IsServerRunning);
        Assert.NotNull(engine.LocalEndPoint);
        Assert.True(engine.LocalEndPoint!.Port > 0);

        await engine.StopServerAsync();
        Assert.False(engine.IsServerRunning);
    }

    [Fact]
    public async Task ClientWriteToOutputSlotLandsInMemoryWhereTheUiReadsIt()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();
        var slot4 = engine.Map.Single(m => m.SlotNumber == 4);

        await using var client = await PlcTestClient.ConnectAsync("127.0.0.1", engine.LocalEndPoint!.Port);
        var res = await client.WriteIndividualAsync((slot4.PointAddress(7).Text, [0x01]));

        Assert.True(res.IsSuccess);
        Assert.True(engine.Memory.ReadBit(slot4.PointAddress(7)));
    }

    [Fact]
    public async Task InputToggleSetByUiIsVisibleToAReadingClient()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();
        var slot2 = engine.Map.Single(m => m.SlotNumber == 2);

        engine.Memory.WriteBit(slot2.PointAddress(5), true);

        await using var client = await PlcTestClient.ConnectAsync("127.0.0.1", engine.LocalEndPoint!.Port);
        var res = await client.ReadIndividualAsync(slot2.PointAddress(5).Text);

        Assert.True(res.IsSuccess);
        Assert.Equal(new byte[] { 0x01 }, res.Blocks[0]);
    }

    [Fact]
    public async Task AnalogChannelWordSetByUiIsReadableByAClient()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();
        var slot5 = engine.Map.Single(m => m.SlotNumber == 5);

        engine.Memory.WriteScalar(slot5.ChannelAddress(3), 2000);

        await using var client = await PlcTestClient.ConnectAsync("127.0.0.1", engine.LocalEndPoint!.Port);
        var res = await client.ReadIndividualAsync(slot5.ChannelAddress(3).Text);

        Assert.Equal(2000, res.FirstWord);
    }

    [Fact]
    public async Task StartingTwiceIsRejected()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartServerAsync());

        await engine.StopServerAsync();
    }

    [Fact]
    public async Task ReconfiguringServerSettingsWhileStoppedTakesEffect()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();
        var firstPort = engine.LocalEndPoint!.Port;
        await engine.StopServerAsync();

        engine.ServerSettings = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 };
        await engine.StartServerAsync();

        Assert.True(engine.IsServerRunning);
        Assert.NotEqual(0, engine.LocalEndPoint!.Port);
        await engine.StopServerAsync();
        Assert.True(firstPort > 0);
    }

    [Fact]
    public async Task ReconfiguringServerSettingsWhileRunningIsRejected()
    {
        using var engine = NewEngine();
        await engine.StartServerAsync();

        Assert.Throws<InvalidOperationException>(
            () => engine.ServerSettings = new ServerSettings { BindAddress = "127.0.0.1", Port = 0 });

        await engine.StopServerAsync();
    }

    [Fact]
    public void EngineRejectsAProjectWhoseConfigurationIsInconsistent()
    {
        var bad = Project() with
        {
            Io = new IoConfiguration
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
            },
        };

        Assert.Throws<IoConfigurationException>(() => new SimulatorEngine(bad, null));
    }

    [Fact]
    public async Task DisposeStopsTheServer()
    {
        var engine = NewEngine();
        await engine.StartServerAsync();
        Assert.True(engine.IsServerRunning);

        engine.Dispose();

        Assert.False(engine.IsServerRunning);
    }
}
