using System.Buffers.Binary;
using System.Text;
using Nxs.Core.Memory;

namespace Nxs.Core.Protocol.Xgt;

/// <summary>
/// XGT FEnet 전용 프로토콜 코덱 — spec/xgt-fenet-reference.md **초안** 구현.
/// </summary>
/// <remarks>
/// <para>
/// 2026-07-30 실제 LabVIEW 애플리케이션과의 현장 검증을 통과했다(접속·개별/연속 읽기·쓰기).
/// 남은 미확인 항목은 spec §5 <b>에러 상태 코드 표</b> 하나 — 거절 응답의 코드 값이라
/// 정상 경로 검증으로는 확인되지 않는다. <see cref="XgtFenetOptions.ErrorCodeMap"/> 으로 교정 가능.
/// </para>
/// <para>
/// 위험을 줄이는 설계 원칙:
/// <list type="number">
/// <item>신뢰도 낮은 헤더 필드(CPUInfo·Position)는 <b>요청 값을 에코</b>한다 — 정답을 몰라도 된다.</item>
/// <item>BCC 는 기본적으로 <b>수신 검사하지 않는다</b> — 틀린 범위로 검사하면 전 요청이 거절된다.</item>
/// <item>에러 코드·쓰기 블록 배치·한계값은 <see cref="XgtFenetOptions"/>로 노출 — 재컴파일 없이 교정.</item>
/// <item>해석 실패는 예외가 아니라 <b>에러 응답</b>이다 — 연결을 유지해 진단을 계속할 수 있게 한다.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class XgtFenetCodec : IFrameCodec
{
    /// <summary>
    /// 이 코덱이 미검증 초안인지.
    /// </summary>
    /// <remarks>
    /// 2026-07-30 저장소 소유자가 실제 LabVIEW 애플리케이션으로 현장 검증을 수행해
    /// 접속·읽기·쓰기가 정상 동작함을 확인했다(spec §8 절차 C). 그에 따라 <c>false</c> 로 내렸다.
    /// <para>
    /// 남은 미확인 항목은 <b>에러 상태 코드 표</b>(spec §5) 하나다 — 정상 경로가 아니라
    /// 거절 응답의 코드 값이므로 현장 검증으로 확인되지 않았다. 매뉴얼 대조가 필요하며,
    /// 그 전까지는 <see cref="XgtFenetOptions.ErrorCodeMap"/> 으로 교정할 수 있다.
    /// </para>
    /// </remarks>
    public const bool IsDraft = false;

    private const ushort CmdReadRequest = 0x0054;
    private const ushort CmdReadResponse = 0x0055;
    private const ushort CmdWriteRequest = 0x0058;
    private const ushort CmdWriteResponse = 0x0059;

    private const ushort TypeBit = 0x0000;
    private const ushort TypeByte = 0x0001;
    private const ushort TypeWord = 0x0002;
    private const ushort TypeDWord = 0x0003;
    private const ushort TypeLWord = 0x0004;
    private const ushort TypeContinuous = 0x0014;

    private static readonly Dictionary<PlcErrorReason, ushort> DefaultErrorCodes = new()
    {
        [PlcErrorReason.None] = 0x0000,
        [PlcErrorReason.InvalidAddress] = 0x0010,
        [PlcErrorReason.RangeExceeded] = 0x0011,
        [PlcErrorReason.UnsupportedDataType] = 0x0012,
        [PlcErrorReason.InvalidBlockCount] = 0x0013,
        [PlcErrorReason.InvalidDataSize] = 0x0014,
        [PlcErrorReason.DataSizeMismatch] = 0x0015,
    };

    private readonly PlcRequestExecutor _executor;
    private readonly XgtFenetOptions _options;
    private readonly AddressingOptions _addressing;

    /// <summary>코덱을 만든다.</summary>
    public XgtFenetCodec(
        PlcRequestExecutor executor,
        XgtFenetOptions? options = null,
        AddressingOptions? addressing = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _options = options ?? XgtFenetOptions.Default;
        _addressing = addressing ?? AddressingOptions.Default;
        LengthRule = new XgtFenetFraming(_options.ValidateCompanyId);
    }

    /// <inheritdoc />
    public IFrameLengthRule LengthRule { get; }

    /// <inheritdoc />
    public int MaxFrameLength => _options.MaxFrameLength;

    /// <inheritdoc />
    public FrameExchange Handle(ReadOnlySpan<byte> requestFrame)
    {
        if (requestFrame.Length < XgtFenetHeader.Length)
        {
            return Malformed(default, CmdReadResponse, "프레임이 헤더 길이보다 짧습니다");
        }

        var header = XgtFenetHeader.Parse(requestFrame[..XgtFenetHeader.Length]);
        var data = requestFrame[XgtFenetHeader.Length..];

        if (_options.ValidateInboundBcc
            && header.Bcc != XgtFenetHeader.ComputeBcc(requestFrame[..XgtFenetHeader.Length]))
        {
            return Malformed(header, CmdReadResponse, "BCC 불일치");
        }

        if (data.Length < 8)
        {
            return Malformed(header, CmdReadResponse, "데이터부가 최소 길이(8바이트)보다 짧습니다");
        }

        var command = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var dataType = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);

        try
        {
            return command switch
            {
                CmdReadRequest => HandleRead(header, dataType, data),
                CmdWriteRequest => HandleWrite(header, dataType, data),
                _ => Malformed(header, CmdReadResponse, $"알 수 없는 명령 코드 0x{command:X4}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException
            or ArgumentException or FormatException)
        {
            // 초안 레이아웃과 실제 프레임이 다르면 여기로 온다 — 연결을 끊지 않고 에러로 답한다.
            var responseCommand = command == CmdWriteRequest ? CmdWriteResponse : CmdReadResponse;
            return Malformed(header, responseCommand, $"데이터부 해석 실패: {ex.Message}");
        }
    }

    private FrameExchange HandleRead(XgtFenetHeader header, ushort dataType, ReadOnlySpan<byte> data)
    {
        var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var cursor = 8;

        if (dataType == TypeContinuous)
        {
            var address = ReadName(data, ref cursor, out var text);
            var byteCount = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);

            var response = _executor.Execute(new ReadContinuousRequest(address, byteCount));
            return BuildReadResponse(header, dataType, response,
                $"연속 읽기 {text} {byteCount}바이트");
        }

        if (!IsScalarType(dataType))
        {
            return Reject(header, CmdReadResponse, dataType, PlcErrorReason.UnsupportedDataType,
                $"알 수 없는 데이터 타입 0x{dataType:X4}");
        }

        var addresses = new List<IecAddress>(blockCount);
        var names = new List<string>(blockCount);
        for (var i = 0; i < blockCount; i++)
        {
            addresses.Add(ReadName(data, ref cursor, out var text));
            names.Add(text);
        }

        var result = _executor.Execute(new ReadIndividualRequest(addresses));
        return BuildReadResponse(header, dataType, result,
            $"개별 읽기 {blockCount}블록: {string.Join(", ", names)}");
    }

    private FrameExchange HandleWrite(XgtFenetHeader header, ushort dataType, ReadOnlySpan<byte> data)
    {
        var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var cursor = 8;

        if (dataType == TypeContinuous)
        {
            var address = ReadName(data, ref cursor, out var text);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
            cursor += 2;
            var payload = data.Slice(cursor, count).ToArray();

            var response = _executor.Execute(new WriteContinuousRequest(address, payload));
            return BuildWriteResponse(header, dataType, response,
                $"연속 쓰기 {text} {count}바이트 = {Hex.Format(payload)}");
        }

        if (!IsScalarType(dataType))
        {
            return Reject(header, CmdWriteResponse, dataType, PlcErrorReason.UnsupportedDataType,
                $"알 수 없는 데이터 타입 0x{dataType:X4}");
        }

        var items = new List<PlcWriteItem>(blockCount);
        var summaries = new List<string>(blockCount);

        if (_options.WriteBlockLayout == XgtWriteBlockLayout.Interleaved)
        {
            for (var i = 0; i < blockCount; i++)
            {
                var address = ReadName(data, ref cursor, out var text);
                var value = ReadValue(data, ref cursor);
                items.Add(new PlcWriteItem(address, value));
                summaries.Add($"{text}={Hex.Format(value)}");
            }
        }
        else
        {
            var addresses = new List<IecAddress>(blockCount);
            var names = new List<string>(blockCount);
            for (var i = 0; i < blockCount; i++)
            {
                addresses.Add(ReadName(data, ref cursor, out var text));
                names.Add(text);
            }

            for (var i = 0; i < blockCount; i++)
            {
                var value = ReadValue(data, ref cursor);
                items.Add(new PlcWriteItem(addresses[i], value));
                summaries.Add($"{names[i]}={Hex.Format(value)}");
            }
        }

        var result = _executor.Execute(new WriteIndividualRequest(items));
        return BuildWriteResponse(header, dataType, result,
            $"개별 쓰기 {blockCount}블록: {string.Join(", ", summaries)}");
    }

    private IecAddress ReadName(ReadOnlySpan<byte> data, ref int cursor, out string text)
    {
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        cursor += 2;
        text = Encoding.ASCII.GetString(data.Slice(cursor, nameLength));
        cursor += nameLength;
        return IecAddress.Parse(text, _addressing);
    }

    private static byte[] ReadValue(ReadOnlySpan<byte> data, ref int cursor)
    {
        var size = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        cursor += 2;
        var value = data.Slice(cursor, size).ToArray();
        cursor += size;
        return value;
    }

    private static bool IsScalarType(ushort dataType)
        => dataType is TypeBit or TypeByte or TypeWord or TypeDWord or TypeLWord;

    private FrameExchange BuildReadResponse(
        XgtFenetHeader header, ushort dataType, PlcResponse response, string requestSummary)
    {
        var blocks = response.IsSuccess ? response.Blocks : [];
        var payloadSize = blocks.Sum(b => 2 + b.Length);
        var data = new byte[10 + payloadSize];

        BinaryPrimitives.WriteUInt16LittleEndian(data, CmdReadResponse);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), dataType);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), ErrorCode(response.Reason));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)blocks.Count);

        var cursor = 10;
        foreach (var block in blocks)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(cursor), (ushort)block.Length);
            cursor += 2;
            block.CopyTo(data.AsSpan(cursor));
            cursor += block.Length;
        }

        return Compose(header, data, requestSummary, response.Reason,
            response.IsSuccess ? $"읽기 응답 · 블록 {blocks.Count}개" : $"거절 · {response.Reason}");
    }

    private FrameExchange BuildWriteResponse(
        XgtFenetHeader header, ushort dataType, PlcResponse response, string requestSummary)
    {
        var data = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CmdWriteResponse);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), dataType);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), ErrorCode(response.Reason));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 0x0000);

        return Compose(header, data, requestSummary, response.Reason,
            response.IsSuccess ? "쓰기 응답 · 정상" : $"거절 · {response.Reason}");
    }

    private FrameExchange Reject(
        XgtFenetHeader header, ushort responseCommand, ushort dataType,
        PlcErrorReason reason, string detail)
    {
        var data = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(data, responseCommand);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), dataType);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), ErrorCode(reason));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 0x0000);

        return Compose(header, data, detail, reason, $"거절 · {reason}");
    }

    private FrameExchange Malformed(XgtFenetHeader header, ushort responseCommand, string detail)
        => Reject(header, responseCommand, 0x0000, PlcErrorReason.InvalidAddress, detail);

    private static FrameExchange Compose(
        XgtFenetHeader header, byte[] data, string requestSummary,
        PlcErrorReason reason, string responseSummary)
    {
        var frame = new byte[XgtFenetHeader.Length + data.Length];
        header.WriteResponse(frame, (ushort)data.Length);
        data.CopyTo(frame, XgtFenetHeader.Length);

        return new FrameExchange
        {
            ResponseFrame = frame,
            RequestSummary = requestSummary,
            ResponseSummary = responseSummary,
            Reason = reason,
        };
    }

    private ushort ErrorCode(PlcErrorReason reason)
    {
        if (_options.ErrorCodeMap is { } map && map.TryGetValue(reason, out var custom))
        {
            return custom;
        }

        return DefaultErrorCodes.TryGetValue(reason, out var code) ? code : (ushort)0x00FF;
    }
}
