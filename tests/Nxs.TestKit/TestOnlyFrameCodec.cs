using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;

namespace Nxs.TestKit;

/// <summary>테스트 전용 합성 코덱의 연산 코드.</summary>
public enum TestOp : byte
{
    /// <summary>개별 읽기.</summary>
    ReadIndividual = 0x01,

    /// <summary>개별 쓰기.</summary>
    WriteIndividual = 0x02,

    /// <summary>연속 읽기.</summary>
    ReadContinuous = 0x03,

    /// <summary>연속 쓰기.</summary>
    WriteContinuous = 0x04,
}

/// <summary>
/// 테스트 전용 합성 코덱. **XGT FEnet 프로토콜이 아니다.**
/// </summary>
/// <remarks>
/// <para>
/// XGT 프레임 근거가 spec 에 없으므로(⛔ M2 blocked-part) 서버 파이프라인
/// (전송 → 프레이밍 → 코덱 → 실행기 → 메모리 → 응답)을 e2e 로 검증하려면 코덱 자리에
/// 무언가가 필요하다. 이 클래스가 그 자리를 채우는 **합성** 구현이며, 자체 발명한 포맷임을
/// 이름·주석으로 명시한다. 실제 코덱이 도착하면 이 클래스는 대체되지 않고 그대로 남아
/// 서버 계층의 회귀 테스트로 계속 쓰인다.
/// </para>
/// <para>
/// 페이로드 레이아웃(합성):
/// <code>
/// 요청  ReadIndividual  : 0x01, count(1), count × { addrLen(1), addrAscii }
/// 요청  WriteIndividual : 0x02, count(1), count × { addrLen(1), addrAscii, valLen(1), val }
/// 요청  ReadContinuous  : 0x03, addrLen(1), addrAscii, byteCount(2 LE)
/// 요청  WriteContinuous : 0x04, addrLen(1), addrAscii, dataLen(2 LE), data
/// 응답                  : (reqOp | 0x80), status(1), blockCount(1), blockCount × { len(2 LE), bytes }
/// </code>
/// </para>
/// </remarks>
public sealed class TestOnlyFrameCodec : IFrameCodec
{
    private readonly PlcRequestExecutor _executor;

    /// <summary>코덱을 만든다.</summary>
    public TestOnlyFrameCodec(PlcRequestExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public IFrameLengthRule LengthRule { get; } = new TestOnlyLengthPrefixFraming();

    /// <inheritdoc />
    public int MaxFrameLength => 8192;

    /// <summary>
    /// 요청이 건드린 주소 표기 — 트래픽 로그의 주소 필터가 이 목록으로 걸러낸다.
    /// </summary>
    private static IReadOnlyList<string> AddressesOf(PlcRequest request) => request switch
    {
        ReadIndividualRequest r => r.Addresses.Select(a => a.ToString()).ToArray(),
        WriteIndividualRequest w => w.Items.Select(i => i.Address.ToString()).ToArray(),
        ReadContinuousRequest r => [r.Start.ToString()],
        WriteContinuousRequest w => [w.Start.ToString()],
        _ => [],
    };

    /// <inheritdoc />
    public FrameExchange Handle(ReadOnlySpan<byte> requestFrame)
    {
        var payload = TestOnlyLengthPrefixFraming.Unwrap(requestFrame);
        if (payload.IsEmpty)
        {
            return Reject(TestOp.ReadIndividual, PlcErrorReason.InvalidBlockCount, "빈 페이로드");
        }

        var op = (TestOp)payload[0];
        try
        {
            var (request, summary) = Decode(op, payload[1..]);
            var response = _executor.Execute(request);
            return new FrameExchange
            {
                ResponseFrame = EncodeResponse(op, response),
                RequestSummary = summary,
                ResponseSummary = response.IsSuccess
                    ? $"OK · 블록 {response.Blocks.Count}개"
                    : $"거절 · {response.Reason}",
                Reason = response.Reason,
                Addresses = AddressesOf(request),
            };
        }
        catch (FormatException ex)
        {
            return Reject(op, PlcErrorReason.InvalidAddress, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Reject(op, PlcErrorReason.UnsupportedDataType, ex.Message);
        }
    }

    /// <summary>개별 읽기 요청 프레임을 만든다(테스트 클라이언트용).</summary>
    public static byte[] BuildReadIndividual(params string[] addresses)
    {
        var body = new List<byte> { (byte)TestOp.ReadIndividual, checked((byte)addresses.Length) };
        foreach (var a in addresses)
        {
            AppendAscii(body, a);
        }

        return TestOnlyLengthPrefixFraming.Wrap(body.ToArray());
    }

    /// <summary>개별 쓰기 요청 프레임을 만든다.</summary>
    public static byte[] BuildWriteIndividual(params (string Address, byte[] Value)[] items)
    {
        var body = new List<byte> { (byte)TestOp.WriteIndividual, checked((byte)items.Length) };
        foreach (var (address, value) in items)
        {
            AppendAscii(body, address);
            body.Add(checked((byte)value.Length));
            body.AddRange(value);
        }

        return TestOnlyLengthPrefixFraming.Wrap(body.ToArray());
    }

    /// <summary>연속 읽기 요청 프레임을 만든다.</summary>
    public static byte[] BuildReadContinuous(string address, int byteCount)
    {
        var body = new List<byte> { (byte)TestOp.ReadContinuous };
        AppendAscii(body, address);
        AppendUInt16(body, byteCount);
        return TestOnlyLengthPrefixFraming.Wrap(body.ToArray());
    }

    /// <summary>연속 쓰기 요청 프레임을 만든다.</summary>
    public static byte[] BuildWriteContinuous(string address, byte[] data)
    {
        var body = new List<byte> { (byte)TestOp.WriteContinuous };
        AppendAscii(body, address);
        AppendUInt16(body, data.Length);
        body.AddRange(data);
        return TestOnlyLengthPrefixFraming.Wrap(body.ToArray());
    }

    /// <summary>응답 프레임을 해석한다.</summary>
    public static TestResponse DecodeResponse(ReadOnlySpan<byte> frame)
    {
        var payload = TestOnlyLengthPrefixFraming.Unwrap(frame);
        var op = (TestOp)(payload[0] & 0x7F);
        var reason = (PlcErrorReason)payload[1];
        var blockCount = payload[2];
        var blocks = new List<byte[]>(blockCount);
        var cursor = 3;
        for (var i = 0; i < blockCount; i++)
        {
            var len = BinaryPrimitives.ReadUInt16LittleEndian(payload[cursor..]);
            cursor += 2;
            blocks.Add(payload.Slice(cursor, len).ToArray());
            cursor += len;
        }

        return new TestResponse(op, reason, blocks);
    }

    private (PlcRequest Request, string Summary) Decode(TestOp op, ReadOnlySpan<byte> body)
    {
        switch (op)
        {
            case TestOp.ReadIndividual:
            {
                var count = body[0];
                var cursor = 1;
                var addresses = new List<IecAddress>(count);
                for (var i = 0; i < count; i++)
                {
                    addresses.Add(ReadAddress(body, ref cursor));
                }

                return (new ReadIndividualRequest(addresses),
                    $"개별 읽기 {count}블록: {string.Join(", ", addresses.Select(a => a.Text))}");
            }

            case TestOp.WriteIndividual:
            {
                var count = body[0];
                var cursor = 1;
                var items = new List<PlcWriteItem>(count);
                for (var i = 0; i < count; i++)
                {
                    var address = ReadAddress(body, ref cursor);
                    var valueLength = body[cursor++];
                    var value = body.Slice(cursor, valueLength).ToArray();
                    cursor += valueLength;
                    items.Add(new PlcWriteItem(address, value));
                }

                return (new WriteIndividualRequest(items),
                    $"개별 쓰기 {count}블록: {string.Join(", ", items.Select(i => $"{i.Address.Text}={Hex.Format(i.Value)}"))}");
            }

            case TestOp.ReadContinuous:
            {
                var cursor = 0;
                var address = ReadAddress(body, ref cursor);
                var byteCount = BinaryPrimitives.ReadUInt16LittleEndian(body[cursor..]);
                return (new ReadContinuousRequest(address, byteCount),
                    $"연속 읽기 {address.Text} {byteCount}바이트");
            }

            case TestOp.WriteContinuous:
            {
                var cursor = 0;
                var address = ReadAddress(body, ref cursor);
                var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(body[cursor..]);
                cursor += 2;
                var data = body.Slice(cursor, dataLength).ToArray();
                return (new WriteContinuousRequest(address, data),
                    $"연속 쓰기 {address.Text} {dataLength}바이트");
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(op), op, $"알 수 없는 합성 연산 코드: 0x{(byte)op:X2}");
        }
    }

    private static IecAddress ReadAddress(ReadOnlySpan<byte> body, ref int cursor)
    {
        var length = body[cursor++];
        var text = Encoding.ASCII.GetString(body.Slice(cursor, length));
        cursor += length;
        return IecAddress.Parse(text);
    }

    private static byte[] EncodeResponse(TestOp op, PlcResponse response)
    {
        var body = new List<byte>
        {
            (byte)((byte)op | 0x80),
            (byte)response.Reason,
            checked((byte)response.Blocks.Count),
        };

        foreach (var block in response.Blocks)
        {
            AppendUInt16(body, block.Length);
            body.AddRange(block);
        }

        return TestOnlyLengthPrefixFraming.Wrap(body.ToArray());
    }

    private static FrameExchange Reject(TestOp op, PlcErrorReason reason, string detail) => new()
    {
        ResponseFrame = EncodeResponse(op, PlcResponse.Failure(reason)),
        RequestSummary = $"해석 실패: {detail}",
        ResponseSummary = $"거절 · {reason}",
        Reason = reason,
    };

    private static void AppendAscii(List<byte> body, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        body.Add(checked((byte)bytes.Length));
        body.AddRange(bytes);
    }

    private static void AppendUInt16(List<byte> body, int value)
    {
        var v = checked((ushort)value);
        body.Add((byte)(v & 0xFF));
        body.Add((byte)(v >> 8));
    }
}

/// <summary>합성 응답 해석 결과.</summary>
/// <param name="Op">응답이 대응하는 요청 연산.</param>
/// <param name="Reason">거절 사유. 성공이면 None.</param>
/// <param name="Blocks">읽기 결과 블록.</param>
public sealed record TestResponse(TestOp Op, PlcErrorReason Reason, IReadOnlyList<byte[]> Blocks)
{
    /// <summary>성공 여부.</summary>
    public bool IsSuccess => Reason == PlcErrorReason.None;

    /// <summary>첫 블록을 리틀엔디안 워드로 해석한다.</summary>
    public ushort FirstWord => BinaryPrimitives.ReadUInt16LittleEndian(Blocks[0]);

    /// <summary>첫 블록을 hex 로 표기한다.</summary>
    public string FirstBlockHex => Hex.Format(Blocks[0]);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Op} {Reason} blocks={Blocks.Count}");
}
