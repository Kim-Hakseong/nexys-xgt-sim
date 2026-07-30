using Nxs.Core.Configuration;

namespace Nxs.Core.Tests.Configuration;

/// <summary>PRD X-05 — AD 채널 공학단위 ↔ raw 변환. DESIGN: 자동화 룰과 스케일 공유.</summary>
public class AnalogChannelScaleTests
{
    private static readonly AnalogChannelScale ZeroToTenVolts = new()
    {
        RawMin = 0,
        RawMax = 4000,
        EngineeringMin = 0,
        EngineeringMax = 10,
        Unit = "V",
    };

    [Fact]
    public void EngineeringMinMapsToRawMin()
        => Assert.Equal(0, ZeroToTenVolts.ToRaw(0));

    [Fact]
    public void EngineeringMaxMapsToRawMax()
        => Assert.Equal(4000, ZeroToTenVolts.ToRaw(10));

    [Fact]
    public void MidScaleMapsToMidRaw()
        => Assert.Equal(2000, ZeroToTenVolts.ToRaw(5));

    [Fact]
    public void RawToEngineeringIsTheInverse()
    {
        Assert.Equal(0d, ZeroToTenVolts.ToEngineering(0));
        Assert.Equal(10d, ZeroToTenVolts.ToEngineering(4000));
        Assert.Equal(5d, ZeroToTenVolts.ToEngineering(2000));
    }

    [Fact]
    public void RoundTripHoldsAcrossTheRange()
    {
        for (var raw = 0; raw <= 4000; raw += 137)
        {
            Assert.Equal(raw, ZeroToTenVolts.ToRaw(ZeroToTenVolts.ToEngineering(raw)));
        }
    }

    [Fact]
    public void EngineeringValueBelowRangeClampsToRawMin()
        => Assert.Equal(0, ZeroToTenVolts.ToRaw(-5));

    [Fact]
    public void EngineeringValueAboveRangeClampsToRawMax()
        => Assert.Equal(4000, ZeroToTenVolts.ToRaw(99));

    [Fact]
    public void BipolarScaleHandlesNegativeEngineeringUnits()
    {
        var bipolar = new AnalogChannelScale
        {
            RawMin = -4000,
            RawMax = 4000,
            EngineeringMin = -10,
            EngineeringMax = 10,
            Unit = "V",
        };

        Assert.Equal(-4000, bipolar.ToRaw(-10));
        Assert.Equal(0, bipolar.ToRaw(0));
        Assert.Equal(4000, bipolar.ToRaw(10));
        Assert.Equal(-5d, bipolar.ToEngineering(-2000));
    }

    [Fact]
    public void RawIsStoredAsSignedWordSoNegativeRawSurvivesWordRoundTrip()
    {
        var bipolar = new AnalogChannelScale
        {
            RawMin = -4000, RawMax = 4000, EngineeringMin = -10, EngineeringMax = 10, Unit = "V",
        };

        var raw = bipolar.ToRaw(-10);
        var word = AnalogChannelScale.RawToWord(raw);
        Assert.Equal(-4000, AnalogChannelScale.WordToRaw(word));
    }

    [Fact]
    public void DegenerateEngineeringRangeIsRejected()
    {
        var bad = new AnalogChannelScale
        {
            RawMin = 0, RawMax = 4000, EngineeringMin = 5, EngineeringMax = 5, Unit = "V",
        };

        Assert.Throws<ArgumentException>(() => bad.ToRaw(5));
    }

    [Fact]
    public void DefaultScaleIsUnityRawPassThrough()
    {
        Assert.Equal(1234, AnalogChannelScale.Default.ToRaw(1234));
        Assert.Equal(1234d, AnalogChannelScale.Default.ToEngineering(1234));
    }
}
