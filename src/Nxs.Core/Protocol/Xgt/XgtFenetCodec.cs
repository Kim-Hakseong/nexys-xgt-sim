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
            if (data.Length - cursor < 2)
            {
                return Reject(header, CmdReadResponse, dataType, PlcErrorReason.InvalidDataSize,
                    $"연속 읽기 {text} 에 바이트 수 필드가 없습니다");
            }

            var byteCount = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);

            var response = _executor.Execute(new ReadContinuousRequest(address, byteCount));
            return BuildReadResponse(header, dataType, response,
                $"연속 읽기 {text} {byteCount}바이트", [text]);
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
            $"개별 읽기 {blockCount}블록: {string.Join(", ", names)}", names);
    }

    private FrameExchange HandleWrite(XgtFenetHeader header, ushort dataType, ReadOnlySpan<byte> data)
    {
        var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var cursor = 8;

        if (dataType == TypeContinuous)
        {
            var address = ReadName(data, ref cursor, out var text);

            // 카운트 필드가 있는지 길이로 판별한다 — 개별 쓰기와 같은 원리.
            //   있음: 남은 = 2 + 선언값  → 선언값이 남은-2 와 일치한다
            //   없음: 남은 = 데이터 전부
            // 무검증으로 앞 2바이트를 카운트로 읽으면 값이 0xFFFF 일 때 65535바이트를 읽으려 해 실패한다.
            var remaining = data.Length - cursor;
            byte[] payload;

            if (remaining >= 2
                && BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]) == remaining - 2)
            {
                var declared = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
                cursor += 2;
                payload = data.Slice(cursor, declared).ToArray();
            }
            else if (remaining > 0)
            {
                payload = data.Slice(cursor, remaining).ToArray();
            }
            else
            {
                return Reject(header, CmdWriteResponse, dataType, PlcErrorReason.InvalidDataSize,
                    $"연속 쓰기 {text} 에 데이터가 없습니다");
            }

            var response = _executor.Execute(new WriteContinuousRequest(address, payload));
            return BuildWriteResponse(header, dataType, response,
                $"연속 쓰기 {text} {payload.Length}바이트 = {Hex.Format(payload)}", [text]);
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

            // 값 구간의 배치를 **프레임 길이로 판별**한다 — 추측하지 않는다.
            //   배치 A: 블록마다 [DataSize(2) + Data(S)]  → 남은 바이트 = N × (2 + S)
            //   배치 B: 크기 필드 없이 [Data(S)]          → 남은 바이트 = N × S
            // 헤더의 Length 필드가 데이터부 크기를 정확히 주므로 산술로 구분된다.
            // (초안 §3 에서 이 배치가 신뢰도 '낮음' 이었던 부분 — 이제 프레임이 스스로 알려준다.)
            var elementSize = ElementSize(dataType);
            var remaining = data.Length - cursor;
            var withSizeField = ChooseValueLayout(remaining, blockCount, elementSize);

            if (withSizeField is null)
            {
                return Reject(header, CmdWriteResponse, dataType, PlcErrorReason.DataSizeMismatch,
                    $"쓰기 값 구간 길이 {remaining}바이트가 블록 {blockCount}개 × 요소 {elementSize}바이트와 "
                    + $"맞지 않습니다 (크기필드 있음={blockCount * (2 + elementSize)} / "
                    + $"없음={blockCount * elementSize})");
            }

            for (var i = 0; i < blockCount; i++)
            {
                var value = withSizeField.Value
                    ? ReadValue(data, ref cursor)
                    : ReadFixedValue(data, ref cursor, elementSize);
                items.Add(new PlcWriteItem(addresses[i], value));
                summaries.Add($"{names[i]}={Hex.Format(value)}");
            }
        }

        var result = _executor.Execute(new WriteIndividualRequest(items));
        return BuildWriteResponse(header, dataType, result,
            $"개별 쓰기 {blockCount}블록: {string.Join(", ", summaries)}",
            items.Select(i => i.Address.Text).ToArray());
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

    /// <summary>크기 필드 없이 고정 폭 값을 읽는다.</summary>
    private static byte[] ReadFixedValue(ReadOnlySpan<byte> data, ref int cursor, int size)
    {
        var value = data.Slice(cursor, size).ToArray();
        cursor += size;
        return value;
    }

    /// <summary>데이터 타입 코드가 뜻하는 요소 바이트 수.</summary>
    private static int ElementSize(ushort dataType) => dataType switch
    {
        TypeBit => 1,
        TypeByte => 1,
        TypeWord => 2,
        TypeDWord => 4,
        TypeLWord => 8,
        _ => 0,
    };

    /// <summary>
    /// 값 구간 길이로 배치를 판별한다. 판별 불가면 null.
    /// </summary>
    /// <remarks>
    /// 두 후보가 같은 길이를 낼 수는 없다(2 &gt; 0 이므로). 따라서 길이가 맞는 쪽이 유일한 해다.
    /// 어느 쪽과도 정확히 맞지 않으면 크기 필드가 있는 배치를 우선 시도하되,
    /// 그 길이보다 짧으면 실패로 본다 — 조용히 오독하는 것보다 정확히 거절하는 편이 낫다.
    /// </remarks>
    private static bool? ChooseValueLayout(int remaining, int blockCount, int elementSize)
    {
        if (blockCount <= 0 || elementSize <= 0)
        {
            return null;
        }

        var withSize = blockCount * (2 + elementSize);
        var withoutSize = blockCount * elementSize;

        if (remaining == withSize)
        {
            return true;
        }

        if (remaining == withoutSize)
        {
            return false;
        }

        // 패딩이 붙은 프레임을 만나도 동작하도록 관용적으로 처리한다.
        if (remaining > withSize)
        {
            return true;
        }

        return remaining > withoutSize ? false : null;
    }

    private static bool IsScalarType(ushort dataType)
        => dataType is TypeBit or TypeByte or TypeWord or TypeDWord or TypeLWord;

    private FrameExchange BuildReadResponse(
        XgtFenetHeader header, ushort dataType, PlcResponse response, string requestSummary,
        IReadOnlyList<string>? addresses = null)
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
            response.IsSuccess ? $"읽기 응답 · 블록 {blocks.Count}개" : $"거절 · {response.Reason}",
            addresses);
    }

    private FrameExchange BuildWriteResponse(
        XgtFenetHeader header, ushort dataType, PlcResponse response, string requestSummary,
        IReadOnlyList<string>? addresses = null)
    {
        var data = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CmdWriteResponse);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), dataType);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), ErrorCode(response.Reason));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 0x0000);

        return Compose(header, data, requestSummary, response.Reason,
            response.IsSuccess ? "쓰기 응답 · 정상" : $"거절 · {response.Reason}", addresses);
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
        PlcErrorReason reason, string responseSummary, IReadOnlyList<string>? addresses = null)
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
            Addresses = addresses ?? [],
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
