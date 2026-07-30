using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Memory;

/// <summary>DESIGN.md 골든 벡터 — 메모리. 수정/삭제 금지.</summary>
public class PlcMemoryTests
{
    [Fact]
    public void WordWriteIsVisibleAsLittleEndianBytesAndBits()
    {
        var mem = new PlcMemory();

        mem.WriteScalar(IecAddress.Parse("%MW0"), 0x0001);

        Assert.Equal(0x01u, mem.ReadScalar(IecAddress.Parse("%MB0")));
        Assert.Equal(0x00u, mem.ReadScalar(IecAddress.Parse("%MB1")));
        Assert.True(mem.ReadBit(IecAddress.Parse("%MX0")));
        Assert.False(mem.ReadBit(IecAddress.Parse("%MX1")));
    }

    [Fact]
    public void TenConsecutiveWordsRoundTrip()
    {
        var mem = new PlcMemory();
        var written = new ushort[10];
        for (var i = 0; i < written.Length; i++)
        {
            written[i] = (ushort)(0x1000 + i * 0x0111);
        }

        mem.WriteWords(MemoryArea.M, 100, written);
        var read = mem.ReadWords(MemoryArea.M, 100, written.Length);

        Assert.Equal(written, read);
    }

    [Fact]
    public void HighByteOfWordMapsToUpperBits()
    {
        var mem = new PlcMemory();

        mem.WriteScalar(IecAddress.Parse("%MW0"), 0x8000);

        Assert.Equal(0x00u, mem.ReadScalar(IecAddress.Parse("%MB0")));
        Assert.Equal(0x80u, mem.ReadScalar(IecAddress.Parse("%MB1")));
        Assert.True(mem.ReadBit(IecAddress.Parse("%MX15")));
    }

    [Fact]
    public void DWordIsLittleEndianAcrossFourBytes()
    {
        var mem = new PlcMemory();

        mem.WriteScalar(IecAddress.Parse("%MD0"), 0x12345678);

        Assert.Equal(0x78u, mem.ReadScalar(IecAddress.Parse("%MB0")));
        Assert.Equal(0x56u, mem.ReadScalar(IecAddress.Parse("%MB1")));
        Assert.Equal(0x34u, mem.ReadScalar(IecAddress.Parse("%MB2")));
        Assert.Equal(0x12u, mem.ReadScalar(IecAddress.Parse("%MB3")));
        Assert.Equal(0x5678u, mem.ReadScalar(IecAddress.Parse("%MW0")));
        Assert.Equal(0x1234u, mem.ReadScalar(IecAddress.Parse("%MW1")));
    }

    [Fact]
    public void AreasAreIndependent()
    {
        var mem = new PlcMemory();

        mem.WriteScalar(IecAddress.Parse("%MW0"), 0xABCD);

        Assert.Equal(0u, mem.ReadScalar(IecAddress.Parse("%IW0")));
        Assert.Equal(0u, mem.ReadScalar(IecAddress.Parse("%QW0")));
    }

    [Fact]
    public void ReadPastAreaEndThrowsAddressRangeException()
    {
        var mem = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 1024 });

        var ex = Assert.Throws<AddressRangeException>(() => mem.ReadWords(MemoryArea.M, 511, 2));
        Assert.Equal(MemoryArea.M, ex.Area);
    }

    [Fact]
    public void WritePastAreaEndThrowsAndLeavesMemoryUnchanged()
    {
        var mem = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 1024 });

        Assert.Throws<AddressRangeException>(() => mem.WriteWords(MemoryArea.M, 511, new ushort[] { 1, 2 }));
        Assert.Equal(0u, mem.ReadScalar(IecAddress.Parse("%MW511")));
    }

    [Fact]
    public void NegativeOffsetThrowsAddressRangeException()
    {
        var mem = new PlcMemory();

        Assert.Throws<AddressRangeException>(() => mem.ReadBytes(MemoryArea.M, -1, 1));
    }

    [Fact]
    public void ConcurrentBitWritesToSharedBytesDoNotLoseUpdates()
    {
        var mem = new PlcMemory();
        const int totalBits = 8 * 2000;

        // 8개 태스크가 같은 바이트를 동시에 건드린다 (bit % 8 로 분할).
        // 읽기-수정-쓰기가 원자적이지 않으면 갱신 유실로 일부 비트가 false 로 남는다.
        Parallel.For(0, 8, lane =>
        {
            for (var bit = lane; bit < totalBits; bit += 8)
            {
                mem.WriteBit(MemoryArea.M, bit, true);
            }
        });

        for (var bit = 0; bit < totalBits; bit++)
        {
            Assert.True(mem.ReadBit(MemoryArea.M, bit), $"비트 {bit} 갱신이 유실되었습니다");
        }
    }

    [Fact]
    public void SlotFormAddressReachesSameCellAsAbsoluteAddress()
    {
        var mem = new PlcMemory();

        mem.WriteBit(IecAddress.Parse("%IX0.2.5"), true);

        Assert.True(mem.ReadBit(IecAddress.Parse("%IX133")));
    }
}
