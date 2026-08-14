using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Xunit;

namespace Nxs.Core.Tests.Protocol;

/// <summary>
/// 개별 쓰기에서 **길이는 실제 온 바이트 수가 정한다** — 이름이 말하는 폭이 아니다.
/// </summary>
/// <remarks>
/// 현장 로그(2026-08-14)에서 잡힌 실패의 원인이다. 마스터는 데이터 타입으로 폭을 정하고
/// 이름은 시작 위치로만 쓰는데, 이름 폭과 값 길이가 다르다는 이유로 거절하면
/// 마스터가 보낸 데이터가 통째로 버려진다.
/// </remarks>
public class WriteWidthFromFrameTests
{
    private static (PlcRequestExecutor Executor, PlcMemory Memory) New()
    {
        var memory = new PlcMemory();
        return (new PlcRequestExecutor(memory), memory);
    }

    [Fact]
    public void FourByteValueAtAWordNameWritesBothWords()
    {
        var (executor, memory) = New();

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MW0"), [0x02, 0x00, 0x00, 0x00]),
        ]));

        Assert.True(response.IsSuccess);
        Assert.Equal(2u, memory.ReadScalar(IecAddress.Parse("%MW0")));
        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW1")));
    }

    [Fact]
    public void TwoByteValueAtADwordNameWritesOnlyThoseTwoBytes()
    {
        var (executor, memory) = New();

        // 반대 방향도 성립해야 한다 — 이름이 넓고 값이 좁은 경우.
        memory.WriteBytes(MemoryArea.M, 0, [0xFF, 0xFF, 0xFF, 0xFF]);

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MD0"), [0x11, 0x22]),
        ]));

        Assert.True(response.IsSuccess);

        // 앞 2바이트만 바뀌고 뒤는 그대로다 — 온 만큼만 쓴다.
        var stored = memory.ReadBytes(MemoryArea.M, 0, 4);
        Assert.Equal(new byte[] { 0x11, 0x22, 0xFF, 0xFF }, stored);
    }

    [Fact]
    public void ExactWidthStillWorksUnchanged()
    {
        var (executor, memory) = New();

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MW10"), [0x34, 0x12]),
        ]));

        Assert.True(response.IsSuccess);
        Assert.Equal(0x1234u, memory.ReadScalar(IecAddress.Parse("%MW10")));
    }

    [Fact]
    public void EmptyValueIsRejectedWithAReadableReason()
    {
        var (executor, _) = New();

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MW0"), []),
        ]));

        Assert.Equal(PlcErrorReason.DataSizeMismatch, response.Reason);
        Assert.Contains("%MW0", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BitAddressStillDemandsExactlyOneByte()
    {
        var (executor, _) = New();

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MX0"), [0x01, 0x00]),
        ]));

        Assert.Equal(PlcErrorReason.DataSizeMismatch, response.Reason);
        Assert.Contains("비트", response.Detail, StringComparison.Ordinal);
        Assert.Contains("2바이트", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BitWriteOfOneByteStillWorks()
    {
        var (executor, memory) = New();

        Assert.True(executor.Execute(new WriteIndividualRequest(
            [new PlcWriteItem(IecAddress.Parse("%MX3"), [0x01])])).IsSuccess);

        Assert.True(memory.ReadBit(IecAddress.Parse("%MX3")));
    }

    [Fact]
    public void AWideWriteThatWouldRunPastMemoryIsRejectedAndWritesNothing()
    {
        var (executor, memory) = New();
        var last = memory.AreaSizeBytes - 2;

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse($"%MB{last}"), [0x11, 0x22, 0x33, 0x44]),
        ]));

        Assert.Equal(PlcErrorReason.RangeExceeded, response.Reason);
        Assert.Contains("4바이트", response.Detail, StringComparison.Ordinal);

        // 검증 후 적용이므로 메모리는 전혀 바뀌지 않아야 한다.
        Assert.Equal(new byte[] { 0x00, 0x00 }, memory.ReadBytes(MemoryArea.M, last, 2));
    }

    [Fact]
    public void OneBadItemRejectsTheWholeRequestWithoutWritingTheGoodOne()
    {
        var (executor, memory) = New();

        var response = executor.Execute(new WriteIndividualRequest(
        [
            new PlcWriteItem(IecAddress.Parse("%MW0"), [0x11, 0x22]),
            new PlcWriteItem(IecAddress.Parse("%MX0"), [0x01, 0x00]),
        ]));

        Assert.False(response.IsSuccess);
        Assert.Equal(0u, memory.ReadScalar(IecAddress.Parse("%MW0")));
    }

    [Fact]
    public void EveryFailureCarriesADetailSoTheLogLineExplainsItself()
    {
        var (executor, _) = New();

        // 사유만 남기고 설명이 비면 현장에서 다시 프레임을 받아 봐야 한다.
        var cases = new PlcRequest[]
        {
            new WriteIndividualRequest([new PlcWriteItem(IecAddress.Parse("%MW0"), [])]),
            new WriteIndividualRequest([new PlcWriteItem(IecAddress.Parse("%MX0"), [0x01, 0x02])]),
            new ReadContinuousRequest(IecAddress.Parse("%MB0"), 999999),
            new WriteContinuousRequest(IecAddress.Parse("%MB0"), new byte[999999]),
        };

        foreach (var request in cases)
        {
            var response = executor.Execute(request);
            Assert.False(response.IsSuccess);
            Assert.NotEmpty(response.Detail);
        }
    }
}
