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
    /// <summary>
    /// 지금 이 스레드에서 묶음 전파 중인 메모리. 전파가 다시 전파를 부르는 것을 막는다.
    /// </summary>
    /// <remarks>
    /// 스레드별로 두는 이유: 전파는 쓴 스레드에서 동기적으로 일어난다. 인스턴스 필드로 두면
    /// A 스레드의 전파가 B 스레드의 정상 쓰기까지 삼켜 버린다.
    /// 값을 인스턴스로 두는 이유: 같은 스레드에서 다른 메모리를 쓰는 경우까지 막을 이유가 없다.
    /// </remarks>
    [ThreadStatic]
    private static PlcMemory? _propagatingFor;

    private readonly object _gate = new();
    private readonly byte[] _inputs;
    private readonly byte[] _outputs;
    private readonly byte[] _internal;
    private volatile MemoryLinks _links = MemoryLinks.Empty;

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

    /// <summary>
    /// 주소 묶음. 한쪽에 값이 들어가면 같은 묶음의 나머지 주소로 퍼진다.
    /// </summary>
    /// <remarks>
    /// 마스터가 쓰든 사용자가 화면에서 쓰든 모든 쓰기가 이 클래스를 지나므로,
    /// 전파를 여기 두면 경로마다 따로 챙길 필요가 없다.
    /// </remarks>
    public MemoryLinks Links
    {
        get => _links;
        set => _links = value ?? MemoryLinks.Empty;
    }

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

        Propagate(area, bitIndex, 1);
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
                DataSize.LWord => throw new ArgumentOutOfRangeException(
                    nameof(address), address.Size,
                    "롱워드(64비트)는 32비트 스칼라로 읽을 수 없습니다 — ReadRaw 를 쓰십시오"),
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
                case DataSize.LWord:
                    throw new ArgumentOutOfRangeException(
                        nameof(address), address.Size,
                        "롱워드(64비트)는 32비트 스칼라로 쓸 수 없습니다 — WriteRaw 를 쓰십시오");
                default:
                    throw new ArgumentOutOfRangeException(nameof(address), address.Size, "알 수 없는 크기 지정자");
            }
        }

        Propagate(address.Area, address.ByteStart * 8, address.ByteLength * 8);
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
        var length = data.Length;
        lock (_gate)
        {
            data.CopyTo(buffer.AsSpan(byteStart, length));
        }

        Propagate(area, byteStart * 8, length * 8);
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

        Propagate(area, byteStart * 8, byteLength * 8);
    }

    /// <summary>주소가 참조하는 바이트를 그대로 읽는다 (엔디안 해석 없음).</summary>
    /// <remarks>비트 주소는 1바이트(0x00 또는 0x01)로 정규화해 반환한다.</remarks>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    public byte[] ReadRaw(IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Size == DataSize.Bit
            ? [ReadBit(address) ? (byte)0x01 : (byte)0x00]
            : ReadBytes(address.Area, address.ByteStart, address.ByteLength);
    }

    /// <summary>주소가 참조하는 바이트를 그대로 쓴다 (엔디안 해석 없음).</summary>
    /// <exception cref="AddressRangeException">범위를 벗어났을 때.</exception>
    /// <exception cref="ArgumentException">길이가 주소 폭과 다를 때.</exception>
    public void WriteRaw(IecAddress address, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Size == DataSize.Bit)
        {
            if (data.Length != 1)
            {
                throw new ArgumentException($"비트 주소에는 1바이트를 써야 합니다. 실제: {data.Length}", nameof(data));
            }

            WriteBit(address, data[0] != 0);
            return;
        }

        if (data.Length != address.ByteLength)
        {
            throw new ArgumentException(
                $"{address.Text} 는 {address.ByteLength}바이트인데 {data.Length}바이트를 주었습니다", nameof(data));
        }

        WriteBytes(address.Area, address.ByteStart, data);
    }

    /// <summary>
    /// 방금 쓴 자리와 겹치는 묶음을 찾아 값을 나머지 멤버로 퍼뜨린다.
    /// </summary>
    /// <remarks>
    /// 락 **밖에서** 부른다 — 전파가 다시 쓰기를 부르므로 락 안에서 부르면 재진입한다
    /// (Monitor 는 재진입을 허용하지만, 그러면 전파 도중의 중간 상태가 다른 읽기에게 보인다).
    /// 대신 각 쓰기가 개별적으로 원자적이다.
    /// </remarks>
    private void Propagate(MemoryArea area, int startBit, int bitCount)
    {
        var links = _links;
        if (links.IsEmpty || ReferenceEquals(_propagatingFor, this))
        {
            return;
        }

        _propagatingFor = this;
        try
        {
            foreach (var (group, source) in links.Affected(area, startBit, bitCount))
            {
                var value = ReadRaw(source);
                foreach (var member in group.Members)
                {
                    if (ReferenceEquals(member, source))
                    {
                        continue;
                    }

                    WriteRaw(member, value);
                }
            }
        }
        finally
        {
            _propagatingFor = null;
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
