using Nxs.Core.Memory;

namespace Nxs.Core.Protocol;

/// <summary>
/// 프로토콜 중립 요청을 메모리에 적용한다 (PRD X-03 의미 절반 · X-04 거절 판정).
/// </summary>
/// <remarks>
/// <para>
/// 거절은 예외가 아니라 <see cref="PlcResponse"/>로 반환된다 — "정확히 거절하는 것"도 실장비 역할이다.
/// 코덱이 이 사유를 spec 기재 에러 코드로 매핑한다(⛔ 게이트).
/// </para>
/// <para>
/// 쓰기는 검증 후 적용(validate-then-apply)이라 거절된 요청은 메모리를 전혀 건드리지 않는다.
/// 개별 쓰기의 각 항목은 개별적으로 원자적이지만 항목 간 원자성은 보장하지 않는다
/// (동시 읽기 클라이언트가 중간 상태를 볼 수 있음 — 실장비도 스캔 단위 갱신이라 동일).
/// </para>
/// </remarks>
public sealed class PlcRequestExecutor
{
    private readonly PlcMemory _memory;
    private readonly PlcRequestLimits _limits;

    /// <summary>실행기를 만든다.</summary>
    public PlcRequestExecutor(PlcMemory memory, PlcRequestLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _limits = limits ?? PlcRequestLimits.Default;
    }

    /// <summary>요청을 실행하고 응답을 만든다. 거절도 정상 반환값이다.</summary>
    public PlcResponse Execute(PlcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request switch
        {
            ReadIndividualRequest r => ReadIndividual(r),
            WriteIndividualRequest r => WriteIndividual(r),
            ReadContinuousRequest r => ReadContinuous(r),
            WriteContinuousRequest r => WriteContinuous(r),
            _ => PlcResponse.Failure(PlcErrorReason.UnsupportedDataType),
        };
    }

    private PlcResponse ReadIndividual(ReadIndividualRequest request)
    {
        if (!IsBlockCountAllowed(request.Addresses.Count))
        {
            return PlcResponse.Failure(PlcErrorReason.InvalidBlockCount);
        }

        // 전 주소를 먼저 검증한다 — 한 주소라도 범위를 벗어나면 요청 전체를 거절한다.
        foreach (var address in request.Addresses)
        {
            if (!IsInRange(address.ByteStart, address.ByteLength))
            {
                return PlcResponse.Failure(PlcErrorReason.RangeExceeded);
            }
        }

        var blocks = new byte[request.Addresses.Count][];
        for (var i = 0; i < blocks.Length; i++)
        {
            var address = request.Addresses[i];
            blocks[i] = address.Size == DataSize.Bit
                ? [_memory.ReadBit(address) ? (byte)0x01 : (byte)0x00]
                : _memory.ReadBytes(address.Area, address.ByteStart, address.ByteLength);
        }

        return PlcResponse.Ok(blocks);
    }

    private PlcResponse WriteIndividual(WriteIndividualRequest request)
    {
        if (!IsBlockCountAllowed(request.Items.Count))
        {
            return PlcResponse.Failure(PlcErrorReason.InvalidBlockCount);
        }

        foreach (var item in request.Items)
        {
            var expected = item.Address.Size == DataSize.Bit ? 1 : item.Address.ByteLength;
            if (item.Value.Length != expected)
            {
                return PlcResponse.Failure(PlcErrorReason.DataSizeMismatch);
            }

            if (!IsInRange(item.Address.ByteStart, item.Address.ByteLength))
            {
                return PlcResponse.Failure(PlcErrorReason.RangeExceeded);
            }
        }

        foreach (var item in request.Items)
        {
            if (item.Address.Size == DataSize.Bit)
            {
                _memory.WriteBit(item.Address, item.Value[0] != 0);
            }
            else
            {
                _memory.WriteBytes(item.Address.Area, item.Address.ByteStart, item.Value);
            }
        }

        return PlcResponse.Ok();
    }

    private PlcResponse ReadContinuous(ReadContinuousRequest request)
    {
        if (!IsContinuousLengthAllowed(request.ByteCount))
        {
            return PlcResponse.Failure(PlcErrorReason.InvalidDataSize);
        }

        if (!IsInRange(request.Start.ByteStart, request.ByteCount))
        {
            return PlcResponse.Failure(PlcErrorReason.RangeExceeded);
        }

        return PlcResponse.Ok([_memory.ReadBytes(request.Start.Area, request.Start.ByteStart, request.ByteCount)]);
    }

    private PlcResponse WriteContinuous(WriteContinuousRequest request)
    {
        if (!IsContinuousLengthAllowed(request.Data.Length))
        {
            return PlcResponse.Failure(PlcErrorReason.InvalidDataSize);
        }

        if (!IsInRange(request.Start.ByteStart, request.Data.Length))
        {
            return PlcResponse.Failure(PlcErrorReason.RangeExceeded);
        }

        _memory.WriteBytes(request.Start.Area, request.Start.ByteStart, request.Data);
        return PlcResponse.Ok();
    }

    private bool IsBlockCountAllowed(int count)
        => count > 0 && (_limits.MaxIndividualBlocks is null || count <= _limits.MaxIndividualBlocks);

    private bool IsContinuousLengthAllowed(int byteCount)
        => byteCount > 0 && (_limits.MaxContinuousBytes is null || byteCount <= _limits.MaxContinuousBytes);

    private bool IsInRange(int byteStart, int byteLength)
        => byteStart >= 0 && byteLength >= 0 && byteStart <= _memory.AreaSizeBytes - byteLength;
}
