using System.Buffers.Binary;

namespace Nxs.Core.Memory;

/// <summary>
/// PLC 메모리맵. 영역별 바이트 배열 + X/B/W/D 접근 뷰(리틀엔디안). 스레드 안전 (PRD X-01).
/// </summary>
/// <remarks>
/// 단일 락으로 전 영역을 보호한다. 비트 쓰기가 읽기-수정-쓰기이므로 같은 바이트를 공유하는
/// 동시 비트 쓰기에서 갱신 유실이 발생하지 않아야 한다.
/// 연속 읽기/쓰기는 스냅샷/원자 적용 — 범위 위반 시 메모리를 건드리지 않는다.
/// </remarks>
public sealed class PlcMemory
{
    private readonly object _gate = new();
    private readonly byte[] _inputs;
    private readonly byte[] _outputs;
    private readonly byte[] _internal;

    /// <summary>메모리를 만든다.</summary>
    public PlcMemory(PlcMemoryOptions? options = null)
    {
        var opts = options ?? PlcMemoryOptions.Default;
        opts.Validate();

        AreaSizeBytes = opts.AreaSizeBytes;
        Addressing = opts.Addressing;
        _inputs = new byte[AreaSizeBytes];
        _outputs = new byte[AreaSizeBytes];
        _internal = new byte[AreaSizeBytes];
    }

    /// <summary>주소 산법 설정.</summary>
    public AddressingOptions Addressing { get; }

    /// <summary>영역별 바이트 크기.</summary>
    public int AreaSizeBytes { get; }

    /// <summary>비트를 읽는다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public bool ReadBit(MemoryArea area, int bitIndex)
    {
        var (byteIndex, mask) = BitLocation(area, bitIndex);
        var buffer = Buffer(area);
        lock (_gate)
        {
            return (buffer[byteIndex] & mask) != 0;
        }
    }

    /// <summary>비트를 쓴다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public void WriteBit(MemoryArea area, int bitIndex, bool value)
    {
        var (byteIndex, mask) = BitLocation(area, bitIndex);
        var buffer = Buffer(area);
        lock (_gate)
        {
            if (value)
            {
                buffer[byteIndex] |= mask;
            }
            else
            {
                buffer[byteIndex] &= (byte)~mask;
            }
        }
    }

    /// <summary>주소가 가리키는 비트를 읽는다.</summary>
    /// <exception cref="InvalidOperationException">비트 주소가 아닐 때.</exception>
    public bool ReadBit(IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        RequireBit(address);
        return ReadBit(address.Area, address.Offset);
    }

    /// <summary>주소가 가리키는 비트를 쓴다.</summary>
    /// <exception cref="InvalidOperationException">비트 주소가 아닐 때.</exception>
    public void WriteBit(IecAddress address, bool value)
    {
        ArgumentNullException.ThrowIfNull(address);
        RequireBit(address);
        WriteBit(address.Area, address.Offset, value);
    }

    /// <summary>주소가 가리키는 스칼라 값을 읽는다. 비트 주소는 0 또는 1.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public uint ReadScalar(IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Size == DataSize.Bit)
        {
            return ReadBit(address) ? 1u : 0u;
        }

        var buffer = Buffer(address.Area);
        EnsureRange(address.Area, address.ByteStart, address.ByteLength);
        lock (_gate)
        {
            var span = buffer.AsSpan(address.ByteStart, address.ByteLength);
            return address.Size switch
            {
                DataSize.Byte => span[0],
                DataSize.Word => BinaryPrimitives.ReadUInt16LittleEndian(span),
                DataSize.DWord => BinaryPrimitives.ReadUInt32LittleEndian(span),
                _ => throw new ArgumentOutOfRangeException(nameof(address), address.Size, "알 수 없는 크기 지정자"),
            };
        }
    }

    /// <summary>주소가 가리키는 스칼라 값을 쓴다. 비트 주소는 0이 아니면 참.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public void WriteScalar(IecAddress address, uint value)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Size == DataSize.Bit)
        {
            WriteBit(address, value != 0);
            return;
        }

        var buffer = Buffer(address.Area);
        EnsureRange(address.Area, address.ByteStart, address.ByteLength);
        lock (_gate)
        {
            var span = buffer.AsSpan(address.ByteStart, address.ByteLength);
            switch (address.Size)
            {
                case DataSize.Byte:
                    span[0] = (byte)value;
                    break;
                case DataSize.Word:
                    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)value);
                    break;
                case DataSize.DWord:
                    BinaryPrimitives.WriteUInt32LittleEndian(span, value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(address), address.Size, "알 수 없는 크기 지정자");
            }
        }
    }

    /// <summary>연속 바이트를 읽는다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public byte[] ReadBytes(MemoryArea area, int byteStart, int count)
    {
        EnsureRange(area, byteStart, count);
        var buffer = Buffer(area);
        var result = new byte[count];
        lock (_gate)
        {
            buffer.AsSpan(byteStart, count).CopyTo(result);
        }

        return result;
    }

    /// <summary>연속 바이트를 쓴다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public void WriteBytes(MemoryArea area, int byteStart, ReadOnlySpan<byte> data)
    {
        EnsureRange(area, byteStart, data.Length);
        var buffer = Buffer(area);
        lock (_gate)
        {
            data.CopyTo(buffer.AsSpan(byteStart, data.Length));
        }
    }

    /// <summary>연속 워드를 읽는다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public ushort[] ReadWords(MemoryArea area, int wordStart, int count)
    {
        var byteStart = ByteStartOfUnit(area, wordStart, bytesPerUnit: 2);
        var byteLength = ByteLengthOfUnits(area, byteStart, count, bytesPerUnit: 2);
        EnsureRange(area, byteStart, byteLength);

        var buffer = Buffer(area);
        var result = new ushort[count];
        lock (_gate)
        {
            var span = buffer.AsSpan(byteStart, byteLength);
            for (var i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]);
            }
        }

        return result;
    }

    /// <summary>연속 워드를 쓴다.</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public void WriteWords(MemoryArea area, int wordStart, ReadOnlySpan<ushort> data)
    {
        var byteStart = ByteStartOfUnit(area, wordStart, bytesPerUnit: 2);
        var byteLength = ByteLengthOfUnits(area, byteStart, data.Length, bytesPerUnit: 2);
        EnsureRange(area, byteStart, byteLength);

        var buffer = Buffer(area);
        lock (_gate)
        {
            var span = buffer.AsSpan(byteStart, byteLength);
            for (var i = 0; i < data.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[(i * 2)..], data[i]);
            }
        }
    }

    /// <summary>영역 전체의 스냅샷을 만든다 (UI 갱신용).</summary>
    public byte[] Snapshot(MemoryArea area)
    {
        var buffer = Buffer(area);
        lock (_gate)
        {
            return buffer.AsSpan().ToArray();
        }
    }

    /// <summary>전 영역을 0으로 지운다.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_inputs);
            Array.Clear(_outputs);
            Array.Clear(_internal);
        }
    }

    private byte[] Buffer(MemoryArea area) => area switch
    {
        MemoryArea.I => _inputs,
        MemoryArea.Q => _outputs,
        MemoryArea.M => _internal,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "알 수 없는 영역"),
    };

    private (int ByteIndex, byte Mask) BitLocation(MemoryArea area, int bitIndex)
    {
        if (bitIndex < 0)
        {
            throw new AddressRangeException(area, bitIndex, 1, AreaSizeBytes);
        }

        var byteIndex = bitIndex / 8;
        EnsureRange(area, byteIndex, 1);
        return (byteIndex, (byte)(1 << (bitIndex % 8)));
    }

    private void EnsureRange(MemoryArea area, int byteStart, int byteLength)
    {
        if (byteStart < 0 || byteLength < 0 || byteStart > AreaSizeBytes - byteLength)
        {
            throw new AddressRangeException(area, byteStart, byteLength, AreaSizeBytes);
        }
    }

    private int ByteStartOfUnit(MemoryArea area, int unitStart, int bytesPerUnit)
    {
        if (unitStart < 0 || unitStart > int.MaxValue / bytesPerUnit)
        {
            throw new AddressRangeException(area, unitStart, bytesPerUnit, AreaSizeBytes);
        }

        return unitStart * bytesPerUnit;
    }

    private int ByteLengthOfUnits(MemoryArea area, int byteStart, int count, int bytesPerUnit)
    {
        if (count < 0 || count > int.MaxValue / bytesPerUnit)
        {
            throw new AddressRangeException(area, byteStart, count, AreaSizeBytes);
        }

        return count * bytesPerUnit;
    }

    private static void RequireBit(IecAddress address)
    {
        if (address.Size != DataSize.Bit)
        {
            throw new InvalidOperationException($"비트 주소가 아닙니다: {address.Text}");
        }
    }
}
