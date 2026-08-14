using System.Buffers.Binary;
using System.Text;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Xunit;

namespace Nxs.Core.Tests.Protocol.Xgt;

/// <summary>
/// 프레임 해부 — "어디부터 어디까지가 무엇인지" 를 구간으로 알려 준다.
/// </summary>
/// <remarks>
/// 진단용이므로 두 가지가 중요하다.
/// 1) 코덱이 실제로 읽는 방식과 설명이 **같아야** 한다 — 어긋나면 진단이 오히려 방해가 된다.
/// 2) 깨진 프레임에서도 **죽지 않아야** 한다 — 깨진 프레임일수록 봐야 할 이유가 크다.
/// </remarks>
public class XgtFrameAnatomyTests
{
    private static byte[] U16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static byte[] Frame(byte[] data, byte direction = 0x33)
    {
        var frame = new byte[20 + data.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        frame[12] = 0xA4;
        frame[13] = direction;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
        byte sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += frame[i];
        }

        frame[19] = sum;
        data.CopyTo(frame, 20);
        return frame;
    }

    private static byte[] ReadRequest(ushort dataType, params string[] names)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0054));
        b.AddRange(U16(dataType));
        b.AddRange(U16(0));
        b.AddRange(U16((ushort)names.Length));
        foreach (var n in names)
        {
            var a = Encoding.ASCII.GetBytes(n);
            b.AddRange(U16((ushort)a.Length));
            b.AddRange(a);
        }

        return b.ToArray();
    }

    private static byte[] WriteRequest(
        ushort dataType, bool withSizeField, params (string Name, byte[] Value)[] items)
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0058));
        b.AddRange(U16(dataType));
        b.AddRange(U16(0));
        b.AddRange(U16((ushort)items.Length));
        foreach (var (name, _) in items)
        {
            var a = Encoding.ASCII.GetBytes(name);
            b.AddRange(U16((ushort)a.Length));
            b.AddRange(a);
        }

        foreach (var (_, value) in items)
        {
            if (withSizeField)
            {
                b.AddRange(U16((ushort)value.Length));
            }

            b.AddRange(value);
        }

        return b.ToArray();
    }

    /// <summary>구간들이 빈틈·겹침 없이 프레임 전체를 덮는지.</summary>
    private static void AssertCoversWholeFrame(IReadOnlyList<FrameField> fields, byte[] frame)
    {
        var expected = 0;
        foreach (var field in fields)
        {
            Assert.Equal(expected, field.Offset);
            Assert.True(field.Length > 0, $"{field.Name} 의 길이가 0이다");
            expected = field.End;
        }

        Assert.Equal(frame.Length, expected);
    }

    [Fact]
    public void HeaderIsBrokenIntoItsEightFields()
    {
        var frame = Frame(ReadRequest(0x0002, "%MW100"));

        var header = XgtFrameAnatomy.Describe(frame)
            .Where(f => f.Kind == FrameFieldKind.Header).ToList();

        Assert.Equal(
            ["회사 ID", "PLC 정보", "CPU 정보", "방향", "Invoke ID", "길이", "모듈 위치", "BCC"],
            header.Select(f => f.Name));

        // 헤더는 정확히 앞 20바이트를 덮는다.
        Assert.Equal(0, header[0].Offset);
        Assert.Equal(20, header[^1].End);
        Assert.Contains("LSIS-XGT", header[0].Value, StringComparison.Ordinal);
        Assert.Contains("요청", header[3].Value, StringComparison.Ordinal);
        Assert.Contains("일치", header[5].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseDirectionAndErrorStatusAreNamed()
    {
        var memory = new PlcMemory();
        var codec = new XgtFenetCodec(new PlcRequestExecutor(memory));
        var response = codec.Handle(Frame(ReadRequest(0x0002, "%MW100"))).ResponseFrame;

        var fields = XgtFrameAnatomy.Describe(response);

        Assert.Contains(fields, f => f.Name == "방향" && f.Value.Contains("응답", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "에러 상태" && f.Value.Contains("정상", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "블록 1 데이터");
        AssertCoversWholeFrame(fields, response);
    }

    [Fact]
    public void RejectedResponseShowsTheErrorStatusAsRejected()
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 64 });
        var codec = new XgtFenetCodec(new PlcRequestExecutor(memory));
        var response = codec.Handle(Frame(ReadRequest(0x0002, "%MW9999"))).ResponseFrame;

        var status = Assert.Single(XgtFrameAnatomy.Describe(response), f => f.Name == "에러 상태");
        Assert.Contains("거절", status.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNameFieldCarriesItsAddressSoTheUiCanHighlightIt()
    {
        var frame = Frame(ReadRequest(0x0002, "%MD310", "%MD311", "%MD312"));

        var fields = XgtFrameAnatomy.Describe(frame);

        var names = fields.Where(f => f.Kind == FrameFieldKind.Name).ToList();
        Assert.Equal(["%MD310", "%MD311", "%MD312"], names.Select(f => f.Address));
        Assert.Equal(["%MD310", "%MD311", "%MD312"], names.Select(f => f.Value));
        AssertCoversWholeFrame(fields, frame);
    }

    [Fact]
    public void LookingUpAnAddressGivesItsByteRange()
    {
        var frame = Frame(ReadRequest(0x0002, "%MD310", "%MD311", "%MD312"));
        var fields = XgtFrameAnatomy.Describe(frame);

        var hit = XgtFrameAnatomy.FieldsFor(fields, "%MD312");

        // 이름 길이 + 변수명 두 구간이 잡혀야 한다.
        Assert.Equal(2, hit.Count);
        var name = hit.Single(f => f.Kind == FrameFieldKind.Name);
        Assert.Equal(6, name.Length);
        Assert.Equal("%MD312", Encoding.ASCII.GetString(frame, name.Offset, name.Length));
    }

    [Fact]
    public void AddressLookupIsCaseInsensitive()
    {
        var fields = XgtFrameAnatomy.Describe(Frame(ReadRequest(0x0002, "%MW100")));
        Assert.NotEmpty(XgtFrameAnatomy.FieldsFor(fields, "%mw100"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteValuesAreNamedAndTiedToTheirAddress(bool withSizeField)
    {
        var frame = Frame(WriteRequest(
            0x0002, withSizeField, ("%MW10", [0x34, 0x12]), ("%MW11", [0x78, 0x56])));

        var fields = XgtFrameAnatomy.Describe(frame);

        var values = fields.Where(f => f.Kind == FrameFieldKind.Value).ToList();
        Assert.Equal(2, values.Count);
        Assert.Equal(["%MW10", "%MW11"], values.Select(f => f.Address));
        Assert.Equal("34 12", values[0].Value);
        Assert.Equal("78 56", values[1].Value);
        AssertCoversWholeFrame(fields, frame);
    }

    [Fact]
    public void FieldCapture_WiderValueThanTheNameIsStillLaidOutCorrectly()
    {
        // 이름 %MW000(2바이트) + 값 4바이트 — 현장에서 관측된 모양.
        var frame = Frame(WriteRequest(0x0003, false, ("%MW000", [0x02, 0x00, 0x00, 0x00])));

        var fields = XgtFrameAnatomy.Describe(frame);

        var value = Assert.Single(fields, f => f.Kind == FrameFieldKind.Value);
        Assert.Equal(4, value.Length);
        Assert.Equal("02 00 00 00", value.Value);
        Assert.Equal("%MW000", value.Address);
        AssertCoversWholeFrame(fields, frame);
    }

    [Fact]
    public void ContinuousReadShowsTheByteCount()
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0054));
        b.AddRange(U16(0x0014));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        var ascii = Encoding.ASCII.GetBytes("%MB0");
        b.AddRange(U16((ushort)ascii.Length));
        b.AddRange(ascii);
        b.AddRange(U16(16));

        var frame = Frame(b.ToArray());
        var fields = XgtFrameAnatomy.Describe(frame);

        var count = Assert.Single(fields, f => f.Name == "읽을 바이트 수");
        Assert.Equal("16", count.Value);
        Assert.Equal("%MB0", count.Address);
        AssertCoversWholeFrame(fields, frame);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContinuousWriteDetectsTheCountFieldTheSameWayTheCodecDoes(bool withCount)
    {
        var payload = new byte[] { 0x11, 0x22, 0x33 };
        var b = new List<byte>();
        b.AddRange(U16(0x0058));
        b.AddRange(U16(0x0014));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        var ascii = Encoding.ASCII.GetBytes("%MB0");
        b.AddRange(U16((ushort)ascii.Length));
        b.AddRange(ascii);
        if (withCount)
        {
            b.AddRange(U16((ushort)payload.Length));
        }

        b.AddRange(payload);

        var frame = Frame(b.ToArray());
        var fields = XgtFrameAnatomy.Describe(frame);

        var data = Assert.Single(fields, f => f.Name == "데이터");
        Assert.Equal("11 22 33", data.Value);
        Assert.Equal(withCount, fields.Any(f => f.Name == "데이터 길이"));
        AssertCoversWholeFrame(fields, frame);
    }

    // ==================== 깨진 입력에서도 죽지 않는다 ====================

    [Fact]
    public void EmptyFrameYieldsNoFieldsRatherThanThrowing()
        => Assert.Empty(XgtFrameAnatomy.Describe([]));

    [Fact]
    public void TruncatedHeaderIsReportedAsSuch()
    {
        var field = Assert.Single(XgtFrameAnatomy.Describe(new byte[5]));
        Assert.Equal(FrameFieldKind.Unknown, field.Kind);
        Assert.Contains("헤더", field.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTruncationOfARealFrameIsDescribedWithoutThrowing()
    {
        var frame = Frame(WriteRequest(0x0002, true, ("%MW10", [0x34, 0x12])));

        // 1바이트씩 잘라 가며 전부 확인한다 — 부분 수신 불변과 같은 취지다.
        for (var length = 0; length <= frame.Length; length++)
        {
            var fields = XgtFrameAnatomy.Describe(frame.AsSpan(0, length));

            var expected = 0;
            foreach (var field in fields)
            {
                Assert.Equal(expected, field.Offset);
                expected = field.End;
            }

            Assert.Equal(length, expected);
        }
    }

    [Fact]
    public void GarbageAfterAValidHeaderIsLeftAsUnknownNotDropped()
    {
        var frame = Frame([0xFF, 0xFF, 0xEE, 0xEE, 0x00, 0x00, 0x01, 0x00, 0xAB, 0xCD]);

        var fields = XgtFrameAnatomy.Describe(frame);

        Assert.Contains(fields, f => f.Kind == FrameFieldKind.Unknown);
        AssertCoversWholeFrame(fields, frame);
    }

    [Fact]
    public void ANameLengthLongerThanTheFrameDoesNotReadPastTheEnd()
    {
        var b = new List<byte>();
        b.AddRange(U16(0x0054));
        b.AddRange(U16(0x0002));
        b.AddRange(U16(0));
        b.AddRange(U16(1));
        b.AddRange(U16(9999));   // 거짓 이름 길이
        b.AddRange(Encoding.ASCII.GetBytes("%MW1"));

        var frame = Frame(b.ToArray());
        var fields = XgtFrameAnatomy.Describe(frame);

        Assert.Contains(fields, f => f.Kind == FrameFieldKind.Unknown);
        AssertCoversWholeFrame(fields, frame);
    }

    [Fact]
    public void ABadBccIsPointedOutRatherThanHidden()
    {
        var frame = Frame(ReadRequest(0x0002, "%MW1"));
        frame[19] ^= 0xFF;

        var bcc = Assert.Single(XgtFrameAnatomy.Describe(frame), f => f.Name == "BCC");
        Assert.Contains("다름", bcc.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ALengthFieldThatDisagreesWithTheFrameIsPointedOut()
    {
        var frame = Frame(ReadRequest(0x0002, "%MW1"));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), 999);

        var length = Assert.Single(XgtFrameAnatomy.Describe(frame), f => f.Name == "길이");
        Assert.Contains("불일치", length.Value, StringComparison.Ordinal);
    }
}
