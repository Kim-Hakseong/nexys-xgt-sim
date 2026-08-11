using System.Buffers.Binary;
using Nxs.Core.Configuration;
using Xunit;

namespace Nxs.Core.Tests.Configuration;

/// <summary>
/// <see cref="WatchValue.ToNumber"/> / <see cref="WatchValue.FromNumber"/> —
/// A/D 채널이 raw ↔ 공학단위를 환산할 때 쓰는 수치 경로.
/// </summary>
/// <remarks>
/// 표시(Render/Parse)와 계산(ToNumber/FromNumber)이 같은 바이트를 다르게 해석하면
/// 화면 값과 메모리 값이 어긋난다. 두 경로의 **일치**를 여기서 고정한다.
/// </remarks>
public class WatchValueNumberTests
{
    [Theory]
    [InlineData(WatchFormat.Signed, 2)]
    [InlineData(WatchFormat.Decimal, 2)]
    [InlineData(WatchFormat.Hex, 2)]
    [InlineData(WatchFormat.Binary, 2)]
    [InlineData(WatchFormat.Signed, 4)]
    [InlineData(WatchFormat.Float, 4)]
    [InlineData(WatchFormat.Double, 8)]
    public void RenderAndToNumberAgreeOnTheSameBytes(WatchFormat format, int byteLength)
    {
        // 형식·폭에 담기는 값을 하나 만들어 왕복시킨다.
        var value = format is WatchFormat.Float or WatchFormat.Double ? 12.5 : 1234;
        var bytes = WatchValue.FromNumber(value, byteLength, format, ByteOrder.Dcba);
        Assert.NotNull(bytes);

        var number = WatchValue.ToNumber(bytes, format, ByteOrder.Dcba);
        Assert.NotNull(number);
        Assert.Equal(value, number.Value, 3);

        // 표시 문자열을 다시 파싱해도 같은 바이트가 나와야 한다.
        var text = WatchValue.Render(bytes, format, ByteOrder.Dcba);
        var reparsed = WatchValue.Parse(text, byteLength, format, ByteOrder.Dcba);
        Assert.Equal(bytes, reparsed);
    }

    [Fact]
    public void FloatBitsReadAsSignedLookLikeGarbageButAsFloatAreExact()
    {
        // 마스터가 100.0f 를 워드 4바이트에 넣은 상황.
        var msb = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(msb, 100.0f);
        var memory = WatchValue.FromMsbFirst(msb, ByteOrder.Dcba);

        var asFloat = WatchValue.ToNumber(memory, WatchFormat.Float, ByteOrder.Dcba);
        var asSigned = WatchValue.ToNumber(memory, WatchFormat.Signed, ByteOrder.Dcba);

        Assert.Equal(100.0, asFloat!.Value, 4);

        // 같은 바이트를 정수로 읽으면 1120403456 — 형식 선택이 필요한 이유가 이것이다.
        Assert.Equal(1120403456, asSigned!.Value);
    }

    [Theory]
    [InlineData("0001 0010 0011 0100", 4660)]
    [InlineData("0b0001001000110100", 4660)]
    [InlineData("1010", 10)]
    public void BinaryDisplayCanBeTypedBackIn(string text, int expected)
    {
        // 2진 표기를 되받지 못하면 화면의 값을 고쳐 쓸 수 없어 형식 선택이 반쪽이 된다.
        var bytes = WatchValue.Parse(text, 2, WatchFormat.Binary, ByteOrder.Dcba);
        Assert.NotNull(bytes);
        Assert.Equal(expected, WatchValue.ToNumber(bytes, WatchFormat.Binary, ByteOrder.Dcba));
    }

    [Theory]
    [InlineData("1234")]                 // 2진에 없는 숫자
    [InlineData("0001 0010 0011 0100 0")] // 폭(16비트) 초과
    [InlineData("")]
    public void BinaryRejectsWhatIsNotBinary(string text)
        => Assert.Null(WatchValue.Parse(text, 2, WatchFormat.Binary, ByteOrder.Dcba));

    [Fact]
    public void NegativeValuesRoundTripInSignedButAreRejectedInUnsigned()
    {
        var signed = WatchValue.FromNumber(-1234, 2, WatchFormat.Signed, ByteOrder.Dcba);
        Assert.NotNull(signed);
        Assert.Equal(-1234, WatchValue.ToNumber(signed, WatchFormat.Signed, ByteOrder.Dcba));

        // 부호 없는 형식에 음수를 담으려 하면 조용히 감싸지 말고 거절해야 한다.
        Assert.Null(WatchValue.FromNumber(-1234, 2, WatchFormat.Decimal, ByteOrder.Dcba));
    }

    [Theory]
    [InlineData(WatchFormat.Signed, 2, 40000)]      // WORD signed 최대 32767 초과
    [InlineData(WatchFormat.Decimal, 2, 70000)]     // WORD unsigned 최대 65535 초과
    public void OutOfRangeIsRejectedRatherThanTruncated(WatchFormat format, int byteLength, double value)
        => Assert.Null(WatchValue.FromNumber(value, byteLength, format, ByteOrder.Dcba));

    [Fact]
    public void FormatThatDoesNotFitTheWidthYieldsNullBothWays()
    {
        // Float 은 4바이트 주소가 필요하다 — 2바이트에서는 수치도 바이트도 만들 수 없다.
        Assert.Null(WatchValue.ToNumber(new byte[2], WatchFormat.Float, ByteOrder.Dcba));
        Assert.Null(WatchValue.FromNumber(1.5, 2, WatchFormat.Float, ByteOrder.Dcba));
        Assert.Null(WatchValue.FromNumber(1.5, 4, WatchFormat.Double, ByteOrder.Dcba));
    }

    [Fact]
    public void IntegerFormatsRoundRatherThanTruncate()
    {
        var bytes = WatchValue.FromNumber(2000.6, 2, WatchFormat.Signed, ByteOrder.Dcba);
        Assert.Equal(2001, WatchValue.ToNumber(bytes!, WatchFormat.Signed, ByteOrder.Dcba));

        var negative = WatchValue.FromNumber(-2000.6, 2, WatchFormat.Signed, ByteOrder.Dcba);
        Assert.Equal(-2001, WatchValue.ToNumber(negative!, WatchFormat.Signed, ByteOrder.Dcba));
    }

    [Fact]
    public void ByteOrderChangesTheStoredBytesButNotTheNumber()
    {
        var abcd = WatchValue.FromNumber(3.25, 4, WatchFormat.Float, ByteOrder.Abcd)!;
        var dcba = WatchValue.FromNumber(3.25, 4, WatchFormat.Float, ByteOrder.Dcba)!;

        Assert.NotEqual(abcd, dcba);
        Assert.Equal(3.25, WatchValue.ToNumber(abcd, WatchFormat.Float, ByteOrder.Abcd)!.Value, 4);
        Assert.Equal(3.25, WatchValue.ToNumber(dcba, WatchFormat.Float, ByteOrder.Dcba)!.Value, 4);

        // 순서를 잘못 고르면 값이 달라진다 — 그래서 사용자가 고를 수 있어야 한다.
        Assert.NotEqual(3.25, WatchValue.ToNumber(dcba, WatchFormat.Float, ByteOrder.Abcd)!.Value, 4);
    }

    [Fact]
    public void NotFiniteValuesAreRejected()
    {
        Assert.Null(WatchValue.FromNumber(double.NaN, 4, WatchFormat.Float, ByteOrder.Dcba));
        Assert.Null(WatchValue.FromNumber(double.PositiveInfinity, 8, WatchFormat.Double, ByteOrder.Dcba));

        // double 로는 유한하지만 float 으로 좁히면 무한이 되는 값도 거절한다.
        Assert.Null(WatchValue.FromNumber(1e40, 4, WatchFormat.Float, ByteOrder.Dcba));
    }
}

/// <summary>실수 raw 를 다루는 스케일 변환.</summary>
public class AnalogChannelScaleRealTests
{
    private static readonly AnalogChannelScale Scale = new()
    {
        RawMin = 0, RawMax = 4000, EngineeringMin = 0, EngineeringMax = 10, Unit = "V",
    };

    [Fact]
    public void RealConversionKeepsTheFractionThatIntegerConversionLoses()
    {
        // raw 2000.5 → 5.00125 V. 정수 경로는 raw 를 깎아 5.0 V 가 된다.
        Assert.Equal(5.00125, Scale.ToEngineering(2000.5), 6);
        Assert.Equal(5.0, Scale.ToEngineering(2000), 6);
    }

    [Fact]
    public void ToRawValueDoesNotRoundButStillClamps()
    {
        Assert.Equal(2000.4, Scale.ToRawValue(5.001), 3);

        // 범위를 넘는 공학단위는 raw 경계로 잡아 둔다.
        Assert.Equal(4000, Scale.ToRawValue(99));
        Assert.Equal(0, Scale.ToRawValue(-99));
    }

    [Fact]
    public void IntegerOverloadStillBehavesExactlyAsBefore()
    {
        // 기존 호출부(정수 raw)는 오버로드 추가로 달라지지 않아야 한다.
        Assert.Equal(2000, Scale.ToRaw(5));
        Assert.Equal(5.0, Scale.ToEngineering(2000));
    }

    [Fact]
    public void ZeroWidthRangeIsReportedNotDividedByZero()
    {
        var flat = new AnalogChannelScale { RawMin = 100, RawMax = 100, EngineeringMin = 0, EngineeringMax = 10 };
        Assert.Throws<ArgumentException>(() => flat.ToEngineering(100.0));

        var flatEu = new AnalogChannelScale { RawMin = 0, RawMax = 4000, EngineeringMin = 5, EngineeringMax = 5 };
        Assert.Throws<ArgumentException>(() => flatEu.ToRawValue(5));
    }
}
