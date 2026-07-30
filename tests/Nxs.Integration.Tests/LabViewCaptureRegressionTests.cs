using Nxs.Core.Fixtures;

namespace Nxs.Integration.Tests;

/// <summary>
/// fixtures/labview-capture/ 회귀 — DESIGN "캡처 회귀" 골든 벡터.
/// </summary>
/// <remarks>
/// <para>
/// 캡처가 있으면 자동 편입되고, 없으면 비활성 상태임을 명시적으로 보고한다
/// (CLAUDE.md §4.3 — "있으면 자동 편입, 없으면 skip").
/// </para>
/// <para>
/// ⛔ 현재 이 스위트는 두 겹으로 막혀 있다: (1) 캡처 파일 부재, (2) XGT 코덱 부재(M2 spec 게이트).
/// 둘 중 하나만 풀려도 부족하다 — 캡처를 해석할 코덱이 없으면 재생할 수 없고, 코덱이 있어도
/// 대조할 실장비 응답이 없으면 정합성을 확인할 수 없다.
/// 하네스 자체의 동작은 <see cref="CaptureReplayTests"/>가 합성 픽스처로 검증한다.
/// </para>
/// </remarks>
public class LabViewCaptureRegressionTests
{
    /// <summary>
    /// XGT FEnet 코덱 팩토리. spec 근거가 기재되면 여기에 실코덱을 연결하면
    /// 아래 회귀가 자동으로 살아난다.
    /// </summary>
    private static Func<Core.Memory.PlcMemory, Core.Protocol.IFrameCodec>? XgtCodecFactory => null;

    public static TheoryData<string> DiscoveredCaptures()
    {
        var data = new TheoryData<string>();
        var directory = CaptureFixtureLoader.FindDirectory();

        var names = directory is null
            ? []
            : CaptureFixtureLoader.Load(directory).Select(c => c.Name).ToArray();

        if (names.Length == 0)
        {
            // xUnit v2 는 데이터가 빈 Theory 를 오류로 처리하므로 센티널 한 건을 넣는다.
            data.Add(NoCaptureSentinel);
            return data;
        }

        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    private const string NoCaptureSentinel = "(캡처 없음)";

    [Theory]
    [MemberData(nameof(DiscoveredCaptures))]
    public void CapturedRequestProducesTheExpectedResponse(string caseName)
    {
        var directory = CaptureFixtureLoader.FindDirectory();

        if (caseName == NoCaptureSentinel)
        {
            // 비활성 경로 — 캡처가 정말 없는지 확인만 하고 통과시킨다.
            var found = directory is null ? [] : CaptureFixtureLoader.Load(directory);
            Assert.Empty(found);
            return;
        }

        Assert.NotNull(directory);
        var captureCase = CaptureFixtureLoader.Load(directory!).Single(c => c.Name == caseName);

        // 캡처가 있는데 코덱이 없으면 조용히 통과시키지 않는다 — 무엇이 막고 있는지 실패로 알린다.
        Assert.False(
            XgtCodecFactory is null,
            $"캡처 '{caseName}' 가 있지만 XGT FEnet 코덱이 없어 재생할 수 없습니다. " +
            "spec/xgt-fenet-reference.md 에 프로토콜 근거를 기재하고 IFrameCodec 구현체를 " +
            $"{nameof(XgtCodecFactory)} 에 연결하십시오. (M2 ⛔ blocked-part)");

        var memory = new Core.Memory.PlcMemory();
        var runner = new CaptureReplayRunner(XgtCodecFactory!(memory));
        var result = runner.Replay(captureCase);

        Assert.False(
            result.Outcome == CaptureReplayOutcome.NoExpectedResponse,
            $"'{caseName}{CaptureFixtureLoader.ExpectedExtension}' 를 작성해야 판정할 수 있습니다. {result.Detail}");

        Assert.Equal(CaptureReplayOutcome.Matched, result.Outcome);
    }

    [Fact]
    public void FixtureDirectoryIsDiscoverableFromTheTestBinary()
    {
        // 캡처를 넣기만 하면 편입되도록, 디렉터리 탐색 자체는 항상 성립해야 한다.
        var directory = CaptureFixtureLoader.FindDirectory();

        Assert.NotNull(directory);
        Assert.True(Directory.Exists(directory));
        Assert.EndsWith("labview-capture", directory!.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
    }

    [Fact]
    public void RegressionSuiteStateIsReportedExplicitly()
    {
        var directory = CaptureFixtureLoader.FindDirectory();
        var cases = directory is null ? [] : CaptureFixtureLoader.Load(directory);

        // 현재 상태를 고정한다. 캡처가 추가되면 이 테스트가 실패해 스위트 활성화를 알린다
        // → 그때 XgtCodecFactory 연결이 필요하다는 신호가 된다.
        Assert.Empty(cases);
        Assert.Null(XgtCodecFactory);
    }
}
