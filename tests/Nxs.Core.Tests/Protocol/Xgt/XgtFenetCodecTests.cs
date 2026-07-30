using System.Buffers.Binary;
using System.Text;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;

namespace Nxs.Core.Tests.Protocol.Xgt;

/// <summary>
/// XGT FEnet 코덱 — spec/xgt-fenet-reference.md **초안**에 대한 벡터.
/// </summary>
/// <remarks>
/// ⚠️ 이 벡터는 매뉴얼 기재 예제가 아니라 초안 레이아웃에서 계산된 것이다.
/// 초안이 틀리면 이 테스트도 함께 틀린다 — 캡처/매뉴얼 검증이 끝나면 실측 벡터로 교체해야 한다.
/// (DESIGN 골든 벡터가 아니므로 교체 가능하다.)
/// </remarks>
public class XgtFenetCodecTests
{
    private const byte DirectionRequest = 0x33;
    private const byte DirectionResponse = 0x11;
    private const byte SampleCpuInfo = 0xA0;
    private const byte SamplePosition = 0x00;

    private static (XgtFenetCodec Codec, PlcMemory Memory) NewCodec(XgtFenetOptions? options = null)
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 8192 });
        return (new XgtFenetCodec(new PlcRequestExecutor(memory), options), memory);
    }

    /// <summary>초안 §1 레이아웃으로 프레임을 만든다.</summary>
    private static byte[] Frame(byte[] data, ushort invokeId = 1, byte cpuInfo = SampleCpuInfo,
        byte position = SamplePosition, byte direction = DirectionRequest, bool validBcc = true)
    {
        var frame = new byte[20 + data.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        // 8..9 는 0x00 패딩
        frame[12] = cpuInfo;
        frame[13] = direction;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14), invokeId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)data.Length);
        frame[18] = position;

        byte sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += frame[i];
        }

        frame[19] = validBcc ? sum : (byte)(sum ^ 0xFF);
        data.CopyTo(frame, 20);
        return frame;
    }

    private static byte[] ReadIndividualData(ushort dataType, params string[] names)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0054));
        body.AddRange(U16(dataType));
        body.AddRange(U16(0x0000));
        body.AddRange(U16((ushort)names.Length));
        foreach (var name in names)
        {
            var ascii = Encoding.ASCII.GetBytes(name);
            body.AddRange(U16((ushort)ascii.Length));
            body.AddRange(ascii);
        }

        return body.ToArray();
    }

    private static byte[] ContinuousReadData(string name, ushort byteCount)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0054));
        body.AddRange(U16(0x0014));
        body.AddRange(U16(0x0000));
        body.AddRange(U16(0x0001));
        var ascii = Encoding.ASCII.GetBytes(name);
        body.AddRange(U16((ushort)ascii.Length));
        body.AddRange(ascii);
        body.AddRange(U16(byteCount));
        return body.ToArray();
    }

    private static byte[] WriteIndividualData(ushort dataType, params (string Name, byte[] Value)[] items)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0058));
        body.AddRange(U16(dataType));
        body.AddRange(U16(0x0000));
        body.AddRange(U16((ushort)items.Length));
        foreach (var (name, _) in items)
        {
            var ascii = Encoding.ASCII.GetBytes(name);
            body.AddRange(U16((ushort)ascii.Length));
            body.AddRange(ascii);
        }

        foreach (var (_, value) in items)
        {
            body.AddRange(U16((ushort)value.Length));
            body.AddRange(value);
        }

        return body.ToArray();
    }

    private static byte[] ContinuousWriteData(string name, byte[] data)
    {
        var body = new List<byte>();
        body.AddRange(U16(0x0058));
        body.AddRange(U16(0x0014));
        body.AddRange(U16(0x0000));
        body.AddRange(U16(0x0001));
        var ascii = Encoding.ASCII.GetBytes(name);
        body.AddRange(U16((ushort)ascii.Length));
        body.AddRange(ascii);
        body.AddRange(U16((ushort)data.Length));
        body.AddRange(data);
        return body.ToArray();
    }

    private static byte[] U16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static ushort ReadU16(ReadOnlySpan<byte> s, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(s[offset..]);

    // ==================== 프레이밍 ====================

    [Fact]
    public void LengthRuleReadsTwentyByteHeaderAndTotalIsHeaderPlusDataLength()
    {
        var rule = new XgtFenetFraming();
        var frame = Frame(ReadIndividualData(0x0002, "%MW100"));

        Assert.Equal(20, rule.HeaderLength);
        Assert.True(rule.TryGetTotalLength(frame.AsSpan(0, 20), out var total));
        Assert.Equal(frame.Length, total);
    }

    [Fact]
    public void LengthRuleRejectsAForeignCompanyId()
    {
        var rule = new XgtFenetFraming();
        var frame = Frame(ReadIndividualData(0x0002, "%MW100"));
        frame[0] = (byte)'X';

        Assert.False(rule.TryGetTotalLength(frame.AsSpan(0, 20), out _));
    }

    [Fact]
    public void RequestSplitAcrossEveryByteBoundaryStillYieldsOneFrame()
    {
        var frame = Frame(ReadIndividualData(0x0002, "%MW100"));

        for (var split = 0; split <= frame.Length; split++)
        {
            var asm = new StreamFrameAssembler(new XgtFenetFraming(), 4096);
            var collected = new List<byte[]>();
            collected.AddRange(asm.Push(frame.AsSpan(0, split)));
            collected.AddRange(asm.Push(frame.AsSpan(split)));

            Assert.Equal(frame, Assert.Single(collected));
        }
    }

    // ==================== 개별 읽기 ====================

    [Fact]
    public void IndividualWordReadReturnsTheStoredValueLittleEndian()
    {
        var (codec, memory) = NewCodec();
        memory.WriteScalar(IecAddress.Parse("%MW100"), 0x1234);

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW100")));
        var response = exchange.ResponseFrame;
        var data = response.AsSpan(20);

        Assert.Equal(PlcErrorReason.None, exchange.Reason);
        Assert.Equal(0x0055, ReadU16(data, 0));       // 읽기 응답
        Assert.Equal(0x0002, ReadU16(data, 2));       // DataType 에코
        Assert.Equal(0x0000, ReadU16(data, 6));       // ErrorStatus 정상
        Assert.Equal(1, ReadU16(data, 8));            // BlockCount
        Assert.Equal(2, ReadU16(data, 10));           // DataSize
        Assert.Equal(0x34, data[12]);
        Assert.Equal(0x12, data[13]);
    }

    [Fact]
    public void ResponseMatchesTheDraftExampleFrameByteForByte()
    {
        // spec 초안 §7.2 예제와 동일해야 한다.
        var (codec, memory) = NewCodec();
        memory.WriteScalar(IecAddress.Parse("%MW100"), 0x1234);

        var response = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW100"), invokeId: 1)).ResponseFrame;

        Assert.Equal(20 + 14, response.Length);
        Assert.Equal("LSIS-XGT", Encoding.ASCII.GetString(response, 0, 8));
        Assert.Equal(0x00, response[8]);
        Assert.Equal(0x00, response[9]);
        Assert.Equal(SampleCpuInfo, response[12]);
        Assert.Equal(DirectionResponse, response[13]);
        Assert.Equal(1, ReadU16(response, 14));
        Assert.Equal(14, ReadU16(response, 16));
        Assert.Equal(
            "55 00 02 00 00 00 00 00 01 00 02 00 34 12",
            Hex.Format(response.AsSpan(20)));
    }

    [Fact]
    public void InvokeIdIsEchoedExactly()
    {
        var (codec, _) = NewCodec();

        foreach (ushort id in new ushort[] { 0, 1, 0x1234, 0xFFFF })
        {
            var response = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW0"), invokeId: id)).ResponseFrame;
            Assert.Equal(id, ReadU16(response, 14));
        }
    }

    [Fact]
    public void CpuInfoAndPositionAreEchoedSoTheCodecNeedNotKnowTheirCorrectValues()
    {
        // 초안에서 신뢰도 '낮음' 인 필드는 단정하지 않고 에코한다.
        var (codec, _) = NewCodec();

        var response = codec.Handle(
            Frame(ReadIndividualData(0x0002, "%MW0"), cpuInfo: 0xA4, position: 0x07)).ResponseFrame;

        Assert.Equal(0xA4, response[12]);
        Assert.Equal(0x07, response[18]);
    }

    [Fact]
    public void ResponseBccIsTheLowByteOfTheHeaderSumOverBytesZeroToEighteen()
    {
        var (codec, _) = NewCodec();

        var response = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW0"))).ResponseFrame;

        byte sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += response[i];
        }

        Assert.Equal(sum, response[19]);
    }

    [Fact]
    public void InboundBccIsNotValidatedByDefaultBecauseItsRangeIsUnverified()
    {
        var (codec, memory) = NewCodec();
        memory.WriteScalar(IecAddress.Parse("%MW0"), 0x00FF);

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW0"), validBcc: false));

        Assert.Equal(PlcErrorReason.None, exchange.Reason);
    }

    [Fact]
    public void InboundBccIsValidatedWhenTheOptionIsEnabled()
    {
        var (codec, _) = NewCodec(new XgtFenetOptions { ValidateInboundBcc = true });

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW0"), validBcc: false));

        Assert.NotEqual(PlcErrorReason.None, exchange.Reason);
    }

    [Fact]
    public void MultiBlockIndividualReadReturnsOneBlockPerName()
    {
        var (codec, memory) = NewCodec();
        memory.WriteScalar(IecAddress.Parse("%MW10"), 0xAAAA);
        memory.WriteScalar(IecAddress.Parse("%MW11"), 0xBBBB);

        var data = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW10", "%MW11")))
            .ResponseFrame.AsSpan(20);

        Assert.Equal(2, ReadU16(data, 8));
        Assert.Equal(2, ReadU16(data, 10));
        Assert.Equal(0xAAAA, ReadU16(data, 12));
        Assert.Equal(2, ReadU16(data, 14));
        Assert.Equal(0xBBBB, ReadU16(data, 16));
    }

    [Fact]
    public void BitReadReturnsSingleByteZeroOrOne()
    {
        var (codec, memory) = NewCodec();
        memory.WriteBit(IecAddress.Parse("%MX801"), true);

        var data = codec.Handle(Frame(ReadIndividualData(0x0000, "%MX801"))).ResponseFrame.AsSpan(20);

        Assert.Equal(1, ReadU16(data, 10));
        Assert.Equal(0x01, data[12]);
    }

    [Fact]
    public void DWordReadReturnsFourBytesLittleEndian()
    {
        var (codec, memory) = NewCodec();
        memory.WriteScalar(IecAddress.Parse("%MD10"), 0x12345678);

        var data = codec.Handle(Frame(ReadIndividualData(0x0003, "%MD10"))).ResponseFrame.AsSpan(20);

        Assert.Equal(4, ReadU16(data, 10));
        Assert.Equal("78 56 34 12", Hex.Format(data.Slice(12, 4)));
    }

    // ==================== 연속 읽기 ====================

    [Fact]
    public void ContinuousReadReturnsRequestedByteCount()
    {
        var (codec, memory) = NewCodec();
        memory.WriteWords(MemoryArea.M, 0, [0x1122, 0x3344, 0x5566]);

        var data = codec.Handle(Frame(ContinuousReadData("%MW0", 6))).ResponseFrame.AsSpan(20);

        Assert.Equal(0x0055, ReadU16(data, 0));
        Assert.Equal(0x0014, ReadU16(data, 2));
        Assert.Equal(0x0000, ReadU16(data, 6));
        Assert.Equal(1, ReadU16(data, 8));
        Assert.Equal(6, ReadU16(data, 10));
        Assert.Equal("22 11 44 33 66 55", Hex.Format(data.Slice(12, 6)));
    }

    // ==================== 쓰기 ====================

    [Fact]
    public void IndividualWordWriteStoresTheValueAndAnswersWithWriteResponse()
    {
        var (codec, memory) = NewCodec();

        var exchange = codec.Handle(Frame(WriteIndividualData(0x0002, ("%MW200", [0x34, 0x12]))));
        var data = exchange.ResponseFrame.AsSpan(20);

        Assert.Equal(PlcErrorReason.None, exchange.Reason);
        Assert.Equal(0x0059, ReadU16(data, 0));
        Assert.Equal(0x0000, ReadU16(data, 6));
        Assert.Equal(0x1234u, memory.ReadScalar(IecAddress.Parse("%MW200")));
    }

    [Fact]
    public void IndividualBitWriteSetsTheBit()
    {
        var (codec, memory) = NewCodec();

        codec.Handle(Frame(WriteIndividualData(0x0000, ("%QX1024", [0x01]))));

        Assert.True(memory.ReadBit(IecAddress.Parse("%QX1024")));
    }

    [Fact]
    public void MultiBlockWriteAppliesEveryBlock()
    {
        var (codec, memory) = NewCodec();

        codec.Handle(Frame(WriteIndividualData(
            0x0002, ("%MW300", [0x01, 0x00]), ("%MW301", [0x02, 0x00]))));

        Assert.Equal(1u, memory.ReadScalar(IecAddress.Parse("%MW300")));
        Assert.Equal(2u, memory.ReadScalar(IecAddress.Parse("%MW301")));
    }

    [Fact]
    public void ContinuousWriteStoresTheBlock()
    {
        var (codec, memory) = NewCodec();

        var exchange = codec.Handle(Frame(ContinuousWriteData("%MW400", [0xAA, 0xBB, 0xCC, 0xDD])));

        Assert.Equal(PlcErrorReason.None, exchange.Reason);
        Assert.Equal(0xBBAAu, memory.ReadScalar(IecAddress.Parse("%MW400")));
        Assert.Equal(0xDDCCu, memory.ReadScalar(IecAddress.Parse("%MW401")));
    }

    // ==================== 오류 ====================

    [Fact]
    public void OutOfRangeReadAnswersWithNonZeroErrorStatusAndKeepsTheCommandCode()
    {
        var (codec, _) = NewCodec();

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW99999")));
        var data = exchange.ResponseFrame.AsSpan(20);

        Assert.Equal(PlcErrorReason.RangeExceeded, exchange.Reason);
        Assert.Equal(0x0055, ReadU16(data, 0));
        Assert.NotEqual(0x0000, ReadU16(data, 6));
        Assert.Equal(0, ReadU16(data, 8));
    }

    [Fact]
    public void UnparsableVariableNameAnswersWithAnErrorNotADisconnect()
    {
        var (codec, _) = NewCodec();

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%ZW10")));

        Assert.Equal(PlcErrorReason.InvalidAddress, exchange.Reason);
        Assert.NotEmpty(exchange.ResponseFrame);
    }

    [Fact]
    public void LWordReadReturnsEightBytesLittleEndian()
    {
        // 데이터 타입 0x0004 = 롱워드. Double 값 지원을 위해 구현했다.
        var (codec, memory) = NewCodec();
        memory.WriteRaw(IecAddress.Parse("%ML10"),
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        var data = codec.Handle(Frame(ReadIndividualData(0x0004, "%ML10"))).ResponseFrame.AsSpan(20);

        Assert.Equal(0x0000, ReadU16(data, 6));
        Assert.Equal(8, ReadU16(data, 10));
        Assert.Equal("01 02 03 04 05 06 07 08", Hex.Format(data.Slice(12, 8)));
    }

    [Fact]
    public void LWordWriteStoresEightBytes()
    {
        var (codec, memory) = NewCodec();

        var exchange = codec.Handle(Frame(WriteIndividualData(
            0x0004, ("%ML20", [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]))));

        Assert.Equal(PlcErrorReason.None, exchange.Reason);
        Assert.Equal("11 22 33 44 55 66 77 88",
            Hex.Format(memory.ReadRaw(IecAddress.Parse("%ML20"))));
    }

    [Fact]
    public void UnknownDataTypeCodeIsStillRejected()
    {
        var (codec, _) = NewCodec();

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0099, "%MW0")));

        Assert.Equal(PlcErrorReason.UnsupportedDataType, exchange.Reason);
    }

    [Fact]
    public void UnknownCommandCodeIsRejected()
    {
        var (codec, _) = NewCodec();
        var data = ReadIndividualData(0x0002, "%MW0");
        data[0] = 0x99;

        var exchange = codec.Handle(Frame(data));

        Assert.NotEqual(PlcErrorReason.None, exchange.Reason);
        Assert.NotEmpty(exchange.ResponseFrame);
    }

    [Fact]
    public void TruncatedDataSectionIsRejectedWithoutThrowing()
    {
        var (codec, _) = NewCodec();
        var full = ReadIndividualData(0x0002, "%MW100");

        var exchange = codec.Handle(Frame(full[..6]));

        Assert.NotEqual(PlcErrorReason.None, exchange.Reason);
    }

    [Fact]
    public void ErrorCodeMapIsOverridableBecauseTheDraftValuesAreUnverified()
    {
        var (codec, _) = NewCodec(new XgtFenetOptions
        {
            ErrorCodeMap = new Dictionary<PlcErrorReason, ushort>
            {
                [PlcErrorReason.RangeExceeded] = 0x7132,
            },
        });

        var data = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW99999"))).ResponseFrame.AsSpan(20);

        Assert.Equal(0x7132, ReadU16(data, 6));
    }

    // ==================== 진단 ====================

    [Fact]
    public void CodecReportsItselfAsDraftUntilTheSpecIsVerified()
        => Assert.True(XgtFenetCodec.IsDraft, "spec 검증이 끝나면 IsDraft 를 false 로 바꾼다");

    [Fact]
    public void SummaryDescribesTheRequestForTheTrafficLog()
    {
        var (codec, _) = NewCodec();

        var exchange = codec.Handle(Frame(ReadIndividualData(0x0002, "%MW100")));

        Assert.Contains("%MW100", exchange.RequestSummary, StringComparison.Ordinal);
        Assert.Contains("읽기", exchange.RequestSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPortIsTwoThousandAndFour()
        => Assert.Equal(2004, XgtFenetOptions.DefaultPort);
}
