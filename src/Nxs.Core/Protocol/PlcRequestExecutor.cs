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
            return PlcResponse.Failure(
                PlcErrorReason.InvalidBlockCount,
                $"개별 읽기 블록 {request.Addresses.Count}개가 허용치 {_limits.MaxIndividualBlocks}개를 넘습니다");
        }

        // 전 주소를 먼저 검증한다 — 한 주소라도 범위를 벗어나면 요청 전체를 거절한다.
        foreach (var address in request.Addresses)
        {
            if (!IsInRange(address.ByteStart, address.ByteLength))
            {
                return PlcResponse.Failure(
                    PlcErrorReason.RangeExceeded,
                    $"{address.Text} 읽기가 메모리 범위를 벗어납니다 "
                    + $"(바이트 {address.ByteStart} ~ {address.ByteStart + address.ByteLength - 1})");
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
            return PlcResponse.Failure(
                PlcErrorReason.InvalidBlockCount,
                $"개별 쓰기 블록 {request.Items.Count}개가 허용치 {_limits.MaxIndividualBlocks}개를 넘습니다");
        }

        foreach (var item in request.Items)
        {
            if (item.Value.Length == 0)
            {
                return PlcResponse.Failure(
                    PlcErrorReason.DataSizeMismatch,
                    $"{item.Address.Text} 에 쓸 값이 비어 있습니다");
            }

            if (item.Address.Size == DataSize.Bit)
            {
                // 비트 쓰기는 1바이트(0/1)여야 한다 — 여러 바이트를 비트에 얹을 방법이 없다.
                if (item.Value.Length != 1)
                {
                    return PlcResponse.Failure(
                        PlcErrorReason.DataSizeMismatch,
                        $"비트 주소 {item.Address.Text} 에는 1바이트만 쓸 수 있는데 "
                        + $"{item.Value.Length}바이트가 왔습니다");
                }

                continue;
            }

            // 이름이 말하는 폭과 실제로 온 값의 길이가 다를 수 있다 — 마스터가 데이터 타입으로
            // 폭을 정하고 이름은 시작 위치로만 쓰는 경우다(예: 이름 %MW000 + DWORD 4바이트).
            // 이름을 근거로 거절하면 마스터가 보낸 데이터가 통째로 버려진다.
            // 시작 위치는 이름이, 길이는 **실제 온 바이트 수**가 정한다.
            if (!IsInRange(item.Address.ByteStart, item.Value.Length))
            {
                return PlcResponse.Failure(
                    PlcErrorReason.RangeExceeded,
                    $"{item.Address.Text} 에서 {item.Value.Length}바이트 쓰기가 메모리 범위를 벗어납니다");
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
            return PlcResponse.Failure(
                PlcErrorReason.InvalidDataSize,
                $"연속 읽기 {request.ByteCount}바이트가 허용치 {_limits.MaxContinuousBytes}바이트를 넘습니다");
        }

        if (!IsInRange(request.Start.ByteStart, request.ByteCount))
        {
            return PlcResponse.Failure(
                PlcErrorReason.RangeExceeded,
                $"{request.Start.Text} 에서 {request.ByteCount}바이트 읽기가 메모리 범위를 벗어납니다");
        }

        return PlcResponse.Ok([_memory.ReadBytes(request.Start.Area, request.Start.ByteStart, request.ByteCount)]);
    }

    private PlcResponse WriteContinuous(WriteContinuousRequest request)
    {
        if (!IsContinuousLengthAllowed(request.Data.Length))
        {
            return PlcResponse.Failure(
                PlcErrorReason.InvalidDataSize,
                $"연속 쓰기 {request.Data.Length}바이트가 허용치 {_limits.MaxContinuousBytes}바이트를 넘습니다");
        }

        if (!IsInRange(request.Start.ByteStart, request.Data.Length))
        {
            return PlcResponse.Failure(
                PlcErrorReason.RangeExceeded,
                $"{request.Start.Text} 에서 {request.Data.Length}바이트 쓰기가 메모리 범위를 벗어납니다");
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
