using Nxs.Core.Memory;
using Nxs.Core.Protocol;

namespace Nxs.Core.Tests.Protocol;

/// <summary>
/// 프로토콜 중립 요청 실행기. 와이어 인코딩(XGT 프레임)은 spec 게이트 대상이므로
/// 이 계층은 "요청 의미 → 메모리 효과 / 거절 사유"만 검증한다 (PRD X-03·X-04의 의미 절반).
/// </summary>
public class PlcRequestExecutorTests
{
    private static (PlcRequestExecutor Exec, PlcMemory Mem) New(PlcRequestLimits? limits = null)
    {
        var mem = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 1024 });
        return (new PlcRequestExecutor(mem, limits), mem);
    }

    [Fact]
    public void ReadIndividualReturnsOneBlockPerAddressSizedByDataSize()
    {
        var (exec, mem) = New();
        mem.WriteScalar(IecAddress.Parse("%MW10"), 0xBEEF);
        mem.WriteScalar(IecAddress.Parse("%MB40"), 0x5A);

        var res = exec.Execute(new ReadIndividualRequest([
            IecAddress.Parse("%MW10"),
            IecAddress.Parse("%MB40"),
        ]));

        Assert.True(res.IsSuccess);
        Assert.Equal(PlcErrorReason.None, res.Reason);
        Assert.Equal(2, res.Blocks.Count);
        Assert.Equal(new byte[] { 0xEF, 0xBE }, res.Blocks[0]);
        Assert.Equal(new byte[] { 0x5A }, res.Blocks[1]);
    }

    [Fact]
    public void ReadIndividualBitReturnsSingleByteZeroOrOne()
    {
        var (exec, mem) = New();
        mem.WriteBit(IecAddress.Parse("%MX3"), true);

        var res = exec.Execute(new ReadIndividualRequest([
            IecAddress.Parse("%MX3"),
            IecAddress.Parse("%MX4"),
        ]));

        Assert.True(res.IsSuccess);
        Assert.Equal(new byte[] { 0x01 }, res.Blocks[0]);
        Assert.Equal(new byte[] { 0x00 }, res.Blocks[1]);
    }

    [Fact]
    public void WriteIndividualAppliesEveryItem()
    {
        var (exec, mem) = New();

        var res = exec.Execute(new WriteIndividualRequest([
            new PlcWriteItem(IecAddress.Parse("%MW20"), new byte[] { 0x34, 0x12 }),
            new PlcWriteItem(IecAddress.Parse("%QX5"), new byte[] { 0x01 }),
        ]));

        Assert.True(res.IsSuccess);
        Assert.Empty(res.Blocks);
        Assert.Equal(0x1234u, mem.ReadScalar(IecAddress.Parse("%MW20")));
        Assert.True(mem.ReadBit(IecAddress.Parse("%QX5")));
    }

    [Fact]
    public void ReadContinuousReturnsSingleBlockOfRequestedByteCount()
    {
        var (exec, mem) = New();
        mem.WriteWords(MemoryArea.M, 0, new ushort[] { 0x1122, 0x3344, 0x5566 });

        var res = exec.Execute(new ReadContinuousRequest(IecAddress.Parse("%MW0"), 6));

        Assert.True(res.IsSuccess);
        Assert.Equal(new byte[] { 0x22, 0x11, 0x44, 0x33, 0x66, 0x55 }, Assert.Single(res.Blocks));
    }

    [Fact]
    public void WriteContinuousWritesFromAddressByteStart()
    {
        var (exec, mem) = New();

        var res = exec.Execute(new WriteContinuousRequest(
            IecAddress.Parse("%MW100"), new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }));

        Assert.True(res.IsSuccess);
        Assert.Equal(0xBBAAu, mem.ReadScalar(IecAddress.Parse("%MW100")));
        Assert.Equal(0xDDCCu, mem.ReadScalar(IecAddress.Parse("%MW101")));
    }

    [Fact]
    public void ReadPastAreaEndIsRejectedWithRangeExceeded()
    {
        var (exec, _) = New();

        var res = exec.Execute(new ReadContinuousRequest(IecAddress.Parse("%MB1020"), 16));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.RangeExceeded, res.Reason);
        Assert.Empty(res.Blocks);
    }

    [Fact]
    public void OneBadAddressRejectsTheWholeReadRequest()
    {
        var (exec, mem) = New();
        mem.WriteScalar(IecAddress.Parse("%MW0"), 0x1111);

        var res = exec.Execute(new ReadIndividualRequest([
            IecAddress.Parse("%MW0"),
            IecAddress.Parse("%MW999"),
        ]));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.RangeExceeded, res.Reason);
        Assert.Empty(res.Blocks);
    }

    [Fact]
    public void RejectedWriteLeavesMemoryCompletelyUnchanged()
    {
        var (exec, mem) = New();

        var res = exec.Execute(new WriteIndividualRequest([
            new PlcWriteItem(IecAddress.Parse("%MW0"), new byte[] { 0xFF, 0xFF }),
            new PlcWriteItem(IecAddress.Parse("%MW999"), new byte[] { 0xFF, 0xFF }),
        ]));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.RangeExceeded, res.Reason);
        // 부분 적용 금지 — 첫 항목도 쓰이지 않아야 한다.
        Assert.Equal(0u, mem.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void WriteLengthComesFromTheFrameNotFromTheNameWidth()
    {
        var (exec, mem) = New();

        // 2026-08-14 현장 로그 이후 바뀐 규칙: 이름은 시작 위치만 정하고 길이는 온 바이트 수가 정한다.
        // 예전에는 이 요청을 DataSizeMismatch 로 거절했는데, 그 때문에 마스터가 보낸 데이터가
        // 통째로 버려졌다(이름 %MW000 + 4바이트 값이 현장에서 실패하던 모양).
        var res = exec.Execute(new WriteIndividualRequest([
            new PlcWriteItem(IecAddress.Parse("%MW0"), new byte[] { 0x01 }),
        ]));

        Assert.True(res.IsSuccess);
        Assert.Equal(1u, mem.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void WriteWithAnEmptyValueIsStillRejectedWithDataSizeMismatch()
    {
        var (exec, mem) = New();

        var res = exec.Execute(new WriteIndividualRequest([
            new PlcWriteItem(IecAddress.Parse("%MW0"), []),
        ]));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.DataSizeMismatch, res.Reason);
        Assert.Equal(0u, mem.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void EmptyIndividualRequestIsRejectedWithInvalidBlockCount()
    {
        var (exec, _) = New();

        var res = exec.Execute(new ReadIndividualRequest([]));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.InvalidBlockCount, res.Reason);
    }

    [Fact]
    public void IndividualBlockCountOverConfiguredLimitIsRejected()
    {
        var (exec, _) = New(new PlcRequestLimits { MaxIndividualBlocks = 2 });

        var res = exec.Execute(new ReadIndividualRequest([
            IecAddress.Parse("%MW0"),
            IecAddress.Parse("%MW1"),
            IecAddress.Parse("%MW2"),
        ]));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.InvalidBlockCount, res.Reason);
    }

    [Fact]
    public void ContinuousByteCountOverConfiguredLimitIsRejected()
    {
        var (exec, _) = New(new PlcRequestLimits { MaxContinuousBytes = 8 });

        var res = exec.Execute(new ReadContinuousRequest(IecAddress.Parse("%MB0"), 9));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.InvalidDataSize, res.Reason);
    }

    [Fact]
    public void DefaultLimitsDoNotRejectLargeRequests()
    {
        // [미정] 실장비 한계값은 spec 미기재 → 기본은 무제한(관용적)이어야 한다.
        // 시뮬레이터가 실장비에 없는 거절을 발명하면 LabVIEW 검증에 거짓 실패가 생긴다.
        var (exec, _) = New();

        var res = exec.Execute(new ReadContinuousRequest(IecAddress.Parse("%MB0"), 1024));

        Assert.True(res.IsSuccess);
        Assert.Equal(1024, Assert.Single(res.Blocks).Length);
    }

    [Fact]
    public void ZeroByteContinuousReadIsRejected()
    {
        var (exec, _) = New();

        var res = exec.Execute(new ReadContinuousRequest(IecAddress.Parse("%MB0"), 0));

        Assert.False(res.IsSuccess);
        Assert.Equal(PlcErrorReason.InvalidDataSize, res.Reason);
    }
}
