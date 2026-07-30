using Nxs.Core.Memory;

namespace Nxs.Core.Tests.Memory;

/// <summary>
/// DESIGN.md 골든 벡터 — 주소 파서. 수정/삭제 금지.
/// slotPts=64 가정(spec/xgi-addressing.md 미확정). 산식 불변.
/// </summary>
public class IecAddressTests
{
    [Fact]
    public void ParsesMw100AsWordOffset100SpanningBytes200To202()
    {
        var a = IecAddress.Parse("%MW100");

        Assert.Equal(MemoryArea.M, a.Area);
        Assert.Equal(DataSize.Word, a.Size);
        Assert.Equal(100, a.Offset);
        Assert.Equal(200, a.ByteStart);
        Assert.Equal(202, a.ByteEnd);
    }

    [Fact]
    public void ParsesMx801AsBit801InByte100Bit1()
    {
        var a = IecAddress.Parse("%MX801");

        Assert.Equal(MemoryArea.M, a.Area);
        Assert.Equal(DataSize.Bit, a.Size);
        Assert.Equal(801, a.Offset);
        Assert.Equal(100, a.ByteStart);
        Assert.Equal(101, a.ByteEnd);
        Assert.Equal(1, a.BitInByte);
    }

    [Fact]
    public void ParsesSlotFormIx0Dot2Dot5AsAbsoluteBit133()
    {
        var a = IecAddress.Parse("%IX0.2.5");

        Assert.Equal(MemoryArea.I, a.Area);
        Assert.Equal(DataSize.Bit, a.Size);
        Assert.Equal(2 * 64 + 5, a.Offset);
        Assert.Equal(133, a.Offset);
    }

    [Fact]
    public void ParsesSlotFormIw0Dot5Dot0AsAbsoluteWord20()
    {
        var a = IecAddress.Parse("%IW0.5.0");

        Assert.Equal(MemoryArea.I, a.Area);
        Assert.Equal(DataSize.Word, a.Size);
        Assert.Equal((5 * 64) / 16, a.Offset);
        Assert.Equal(20, a.Offset);
    }

    [Fact]
    public void RejectsUnsupportedAreaZ()
    {
        Assert.False(IecAddress.TryParse("%ZW10", out _));
        Assert.Throws<FormatException>(() => IecAddress.Parse("%ZW10"));
    }

    [Fact]
    public void RejectsTwoComponentSlotForm()
    {
        Assert.False(IecAddress.TryParse("%IX0.2", out _));
        Assert.Throws<FormatException>(() => IecAddress.Parse("%IX0.2"));
    }
}
