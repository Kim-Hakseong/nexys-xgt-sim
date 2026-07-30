using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Automation;

/// <summary>자동화 룰의 .nxp 직렬화 라운드트립 (PRD X-06 + X-08).</summary>
public class AutomationRuleSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nxsim-rules-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RampSettingsBuildTheGoldenVectorGenerator()
    {
        var rule = new AutomationRuleSettings
        {
            Address = "%MW0",
            Kind = GeneratorKind.Ramp,
            Min = 0,
            Max = 100,
            Step = 25,
            PeriodMs = 500,
        }.ToRule();

        Assert.Equal(
            new[] { 0, 25, 50, 75, 100, 0 },
            Enumerable.Range(0, 6).Select(rule.Generator.ValueAt).ToArray());
        Assert.Equal(TimeSpan.FromMilliseconds(500), rule.Period);
    }

    [Fact]
    public void EveryGeneratorKindRoundTripsThroughSettings()
    {
        IValueGenerator[] generators =
        [
            new FixedGenerator { Value = 42 },
            new IncrementGenerator { Min = 1, Max = 9, Step = 2 },
            new RampGenerator { Min = 0, Max = 100, Step = 25 },
            new SineGenerator { Min = 0, Max = 1000, Period = 4 },
            new RandomGenerator { Min = 5, Max = 15, Seed = 99 },
            new ToggleGenerator(),
        ];

        foreach (var generator in generators)
        {
            var original = new AutomationRule
            {
                Target = IecAddress.Parse("%MW0"),
                Generator = generator,
                Period = TimeSpan.FromMilliseconds(250),
            };

            var restored = AutomationRuleSettings.FromRule(original).ToRule();

            Assert.Equal(generator.Kind, restored.Generator.Kind);
            Assert.Equal(
                Enumerable.Range(0, 12).Select(generator.ValueAt).ToArray(),
                Enumerable.Range(0, 12).Select(restored.Generator.ValueAt).ToArray());
            Assert.Equal(original.Period, restored.Period);
        }
    }

    [Fact]
    public void RulesSurviveNxpSaveAndLoad()
    {
        var path = Path.Combine(_dir, "rules.nxp");
        var project = NxpProject.CreateDefault(port: 2004) with
        {
            AutomationRules =
            [
                new AutomationRuleSettings
                {
                    Address = "%MW100", Kind = GeneratorKind.Sine, Min = 0, Max = 1000, Period = 4, PeriodMs = 200,
                },
                new AutomationRuleSettings
                {
                    Address = "%MX200", Kind = GeneratorKind.Toggle, PeriodMs = 1000, IsEnabled = false,
                },
            ],
        };

        NxpProjectFile.Save(path, project);
        var loaded = NxpProjectFile.Load(path);

        Assert.Equal(2, loaded.AutomationRules.Count);
        Assert.Equal(GeneratorKind.Sine, loaded.AutomationRules[0].Kind);
        Assert.Equal("%MW100", loaded.AutomationRules[0].Address);
        Assert.Equal(200, loaded.AutomationRules[0].PeriodMs);
        Assert.False(loaded.AutomationRules[1].IsEnabled);

        var rules = loaded.BuildAutomationRules();
        Assert.Equal(new[] { 500, 1000, 500, 0 }, Enumerable.Range(0, 4).Select(rules[0].Generator.ValueAt));
    }

    [Fact]
    public void EngineeringUnitRuleOnAnAdChannelPicksUpThatChannelScale()
    {
        var project = NxpProject.CreateDefault(port: 2004) with
        {
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
            AutomationRules =
            [
                new AutomationRuleSettings
                {
                    // 슬롯5 채널0 = %IW80
                    Address = "%IW80",
                    Kind = GeneratorKind.Ramp,
                    Min = 0,
                    Max = 10,
                    Step = 5,
                    PeriodMs = 100,
                    UseEngineeringUnits = true,
                },
            ],
        };

        var rule = Assert.Single(project.BuildAutomationRules());

        Assert.NotNull(rule.Scale);
        Assert.Equal(10, rule.Scale!.EngineeringMax);
        Assert.Equal("V", rule.Scale.Unit);
        Assert.Equal(2000, rule.Scale.ToRaw(5));
    }

    [Fact]
    public void EngineeringUnitRuleOnANonChannelAddressHasNoScale()
    {
        var project = NxpProject.CreateDefault(port: 2004) with
        {
            AutomationRules =
            [
                new AutomationRuleSettings
                {
                    Address = "%MW500",
                    Kind = GeneratorKind.Ramp,
                    Min = 0, Max = 10, Step = 5, PeriodMs = 100,
                    UseEngineeringUnits = true,
                },
            ],
        };

        Assert.Null(Assert.Single(project.BuildAutomationRules()).Scale);
    }

    [Fact]
    public void ProjectWithoutRulesBuildsAnEmptyRuleSet()
        => Assert.Empty(NxpProject.CreateDefault(port: 2004).BuildAutomationRules());

    [Fact]
    public void RuleWithAnUnparsableAddressIsRejected()
    {
        var project = NxpProject.CreateDefault(port: 2004) with
        {
            AutomationRules =
            [
                new AutomationRuleSettings { Address = "%ZW1", Kind = GeneratorKind.Toggle, PeriodMs = 100 },
            ],
        };

        Assert.Throws<FormatException>(() => project.BuildAutomationRules());
    }

    [Fact]
    public void RuleWithZeroPeriodIsRejected()
    {
        var settings = new AutomationRuleSettings
        {
            Address = "%MW0", Kind = GeneratorKind.Toggle, PeriodMs = 0,
        };

        Assert.Throws<ArgumentException>(() => settings.ToRule());
    }

    [Fact]
    public void OlderNxpWithoutTheRulesKeyStillLoads()
    {
        // JSON 은 가산적 — M4 시절 파일(자동화 절이 아예 없음)도 포맷 버전 상승 없이 열려야 한다.
        var path = Path.Combine(_dir, "legacy.nxp");
        File.WriteAllText(path, """
            {
              "formatVersion": 1,
              "io": {
                "addressing": { "slotPoints": 256, "slotsPerBase": 12 },
                "bases": [
                  {
                    "baseNumber": 0,
                    "slots": [
                      {
                        "slotNumber": 2,
                        "module": {
                          "productName": "XGI-D24A",
                          "kind": "DigitalInput",
                          "pointCount": 32,
                          "channelCount": 0,
                          "description": "DC 입력 32점"
                        }
                      }
                    ]
                  }
                ]
              },
              "server": { "bindAddress": "0.0.0.0", "port": 2004 },
              "initialValues": [],
              "analogChannels": []
            }
            """);

        var loaded = NxpProjectFile.Load(path);

        Assert.Empty(loaded.AutomationRules);
        Assert.Equal(2004, loaded.Server.Port);
        Assert.Equal("XGI-D24A", loaded.Io.Bases.Single().Slots.Single().Module!.ProductName);
        Assert.Equal(512, loaded.Io.BuildMap().Single().StartBit);
    }
}
