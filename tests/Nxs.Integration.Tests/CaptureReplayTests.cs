using Nxs.Core.Fixtures;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.TestKit;

namespace Nxs.Integration.Tests;

/// <summary>
/// 캡처 재생 하네스 자체의 검증.
/// </summary>
/// <remarks>
/// fixtures/labview-capture/ 는 현재 비어 있으므로(실캡처 부재) 하네스가 **동작한다는 증거**를
/// 합성 픽스처로 확보한다. 이것이 없으면 M7은 "빈 디렉터리를 훑고 통과하는" 무의미한 테스트가 된다.
/// 실캡처가 도착하면 같은 코드가 그것을 검사한다.
/// </remarks>
public class CaptureReplayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nxsim-capture-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (CaptureReplayRunner Runner, PlcMemory Memory) NewRunner()
    {
        var memory = new PlcMemory(new PlcMemoryOptions { AreaSizeBytes = 4096 });
        return (new CaptureReplayRunner(new TestOnlyFrameCodec(new PlcRequestExecutor(memory))), memory);
    }

    private void WriteCase(string name, byte[] request, byte[]? expected)
    {
        File.WriteAllBytes(Path.Combine(_dir, $"{name}.bin"), request);
        if (expected is not null)
        {
            File.WriteAllBytes(Path.Combine(_dir, $"{name}.expected"), expected);
        }
    }

    [Fact]
    public void LoaderFindsRequestFilesAndTheirExpectedResponses()
    {
        WriteCase("req_read", TestOnlyFrameCodec.BuildReadIndividual("%MW0"), [0x01, 0x02]);
        WriteCase("req_write", TestOnlyFrameCodec.BuildWriteIndividual(("%MW1", [0x01, 0x00])), null);

        var cases = CaptureFixtureLoader.Load(_dir);

        Assert.Equal(2, cases.Count);
        var read = cases.Single(c => c.Name == "req_read");
        Assert.True(read.HasExpectedResponse);
        Assert.Equal(new byte[] { 0x01, 0x02 }, read.ExpectedResponse);
        Assert.False(cases.Single(c => c.Name == "req_write").HasExpectedResponse);
    }

    [Fact]
    public void LoaderReturnsNothingForAnEmptyDirectory()
        => Assert.Empty(CaptureFixtureLoader.Load(_dir));

    [Fact]
    public void LoaderReturnsNothingForAMissingDirectory()
        => Assert.Empty(CaptureFixtureLoader.Load(Path.Combine(_dir, "does-not-exist")));

    [Fact]
    public void LoaderOrdersCasesByNameForDeterministicReplay()
    {
        WriteCase("req_c", TestOnlyFrameCodec.BuildReadIndividual("%MW2"), null);
        WriteCase("req_a", TestOnlyFrameCodec.BuildReadIndividual("%MW0"), null);
        WriteCase("req_b", TestOnlyFrameCodec.BuildReadIndividual("%MW1"), null);

        Assert.Equal(
            new[] { "req_a", "req_b", "req_c" },
            CaptureFixtureLoader.Load(_dir).Select(c => c.Name));
    }

    [Fact]
    public void ReplayOfAMatchingCasePasses()
    {
        var (runner, memory) = NewRunner();
        memory.WriteScalar(IecAddress.Parse("%MW10"), 0xBEEF);
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW10");
        var expected = Probe(runner, request);

        var result = runner.Replay(new CaptureCase("req_read", request, expected));

        Assert.Equal(CaptureReplayOutcome.Matched, result.Outcome);
        Assert.Equal(expected, result.ActualResponse);
    }

    [Fact]
    public void ReplayOfAMismatchingCaseFailsAndShowsBothSides()
    {
        var (runner, memory) = NewRunner();
        memory.WriteScalar(IecAddress.Parse("%MW10"), 0x1111);
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW10");
        var wrongExpected = Probe(runner, request);
        wrongExpected[^1] ^= 0xFF;

        var result = runner.Replay(new CaptureCase("req_read", request, wrongExpected));

        Assert.Equal(CaptureReplayOutcome.Mismatched, result.Outcome);
        Assert.Contains("기대", result.Detail, StringComparison.Ordinal);
        Assert.Contains(Hex.Format(result.ActualResponse), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayWithoutAnExpectedFileIsReportedAsPendingNotPassing()
    {
        var (runner, _) = NewRunner();
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW0");

        var result = runner.Replay(new CaptureCase("req_read", request, null));

        Assert.Equal(CaptureReplayOutcome.NoExpectedResponse, result.Outcome);
        Assert.NotEmpty(result.ActualResponse);
        // 사람이 .expected 를 작성할 수 있도록 실제 응답 hex 를 알려준다.
        Assert.Contains(Hex.Format(result.ActualResponse), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayFeedsTheRequestOneByteAtATimeSoItAlsoProvesPartialReceive()
    {
        var (runner, memory) = NewRunner();
        memory.WriteScalar(IecAddress.Parse("%MW5"), 0x0A0B);
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW5");
        var expected = Probe(runner, request);

        var result = runner.Replay(new CaptureCase("req_read", request, expected));

        Assert.Equal(CaptureReplayOutcome.Matched, result.Outcome);
        Assert.Equal(request.Length, result.BytesFedOneAtATime);
    }

    [Fact]
    public void ReplayOfACaptureContainingTwoRequestsConcatenatesBothResponses()
    {
        var (runner, memory) = NewRunner();
        memory.WriteScalar(IecAddress.Parse("%MW1"), 0x1111);
        memory.WriteScalar(IecAddress.Parse("%MW2"), 0x2222);
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW1")
            .Concat(TestOnlyFrameCodec.BuildReadIndividual("%MW2")).ToArray();
        var expected = Probe(runner, request);

        var result = runner.Replay(new CaptureCase("two", request, expected));

        Assert.Equal(CaptureReplayOutcome.Matched, result.Outcome);
        Assert.Equal(2, result.FrameCount);
    }

    [Fact]
    public void ReplayOfAnUnframeableCaptureIsReportedNotThrown()
    {
        var (runner, _) = NewRunner();

        var result = runner.Replay(new CaptureCase("garbage", [0x00, 0x01, 0x02, 0x03], null));

        Assert.Equal(CaptureReplayOutcome.FramingError, result.Outcome);
        Assert.NotEmpty(result.Detail);
    }

    [Fact]
    public void ReplayOfATruncatedCaptureIsReportedAsIncomplete()
    {
        var (runner, _) = NewRunner();
        var full = TestOnlyFrameCodec.BuildReadIndividual("%MW0");

        var result = runner.Replay(new CaptureCase("truncated", full[..^2], null));

        Assert.Equal(CaptureReplayOutcome.IncompleteFrame, result.Outcome);
    }

    [Fact]
    public void EndToEndLoadAndReplayOverAGeneratedFixtureDirectory()
    {
        var (runner, memory) = NewRunner();
        memory.WriteScalar(IecAddress.Parse("%MW20"), 0x4321);
        var request = TestOnlyFrameCodec.BuildReadIndividual("%MW20");
        WriteCase("req_generated", request, Probe(runner, request));

        var results = CaptureFixtureLoader.Load(_dir).Select(runner.Replay).ToArray();

        Assert.Equal(CaptureReplayOutcome.Matched, Assert.Single(results).Outcome);
    }

    /// <summary>
    /// 같은 러너로 요청을 한 번 흘려 기대 응답을 코드로 만든다 (placeholder 금지 — CLAUDE.md §4).
    /// 읽기 요청은 멱등이므로 같은 러너에 두 번 흘려도 같은 응답이 나온다.
    /// 실제 운용에서는 사람이 매뉴얼 대조로 .expected 를 작성한다.
    /// </summary>
    private static byte[] Probe(CaptureReplayRunner runner, byte[] request)
        => runner.Replay(new CaptureCase("probe", request, null)).ActualResponse;
}
