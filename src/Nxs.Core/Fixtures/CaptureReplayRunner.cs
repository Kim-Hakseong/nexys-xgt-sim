using Nxs.Core.Protocol;

namespace Nxs.Core.Fixtures;

/// <summary>
/// 캡처된 요청을 시뮬레이터 코덱에 재생해 기대 응답과 대조한다 (PRD M7 — 실장비 대역 회귀).
/// </summary>
/// <remarks>
/// 요청 바이트를 **1바이트씩** 주입하므로 캡처 회귀가 부분 수신 불변까지 함께 검증한다
/// (CLAUDE.md §4.2). 실장비가 없는 환경에서 "완성된 LabVIEW 코드가 이미 정답 요청 프레임을 생성한다"는
/// 사실을 검증 자산으로 쓰는 지점이다 (CONTEXT "검증의 왕도").
/// </remarks>
public sealed class CaptureReplayRunner
{
    private readonly IFrameCodec _codec;

    /// <summary>러너를 만든다.</summary>
    public CaptureReplayRunner(IFrameCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    /// <summary>케이스 하나를 재생한다. 실패도 예외가 아닌 결과로 반환한다.</summary>
    public CaptureReplayResult Replay(CaptureCase captureCase)
    {
        ArgumentNullException.ThrowIfNull(captureCase);

        var assembler = new StreamFrameAssembler(_codec.LengthRule, _codec.MaxFrameLength);
        var responses = new List<byte>();
        var frameCount = 0;
        var fed = 0;
        var oneByte = new byte[1];

        foreach (var b in captureCase.RequestBytes)
        {
            oneByte[0] = b;
            IReadOnlyList<byte[]> frames;
            try
            {
                frames = assembler.Push(oneByte);
            }
            catch (FramingException ex)
            {
                return new CaptureReplayResult
                {
                    Name = captureCase.Name,
                    Outcome = CaptureReplayOutcome.FramingError,
                    FrameCount = frameCount,
                    BytesFedOneAtATime = fed,
                    ActualResponse = responses.ToArray(),
                    Detail = $"{fed + 1}번째 바이트에서 프레이밍 위반: {ex.Message}",
                };
            }

            fed++;

            foreach (var frame in frames)
            {
                frameCount++;
                var exchange = _codec.Handle(frame);
                responses.AddRange(exchange.ResponseFrame);
            }
        }

        var actual = responses.ToArray();

        if (assembler.BufferedByteCount > 0)
        {
            return new CaptureReplayResult
            {
                Name = captureCase.Name,
                Outcome = CaptureReplayOutcome.IncompleteFrame,
                FrameCount = frameCount,
                BytesFedOneAtATime = fed,
                ActualResponse = actual,
                Detail = $"캡처가 프레임 중간에서 끝났습니다 — 보류 {assembler.BufferedByteCount}바이트. "
                    + "캡처가 잘렸는지 확인하십시오.",
            };
        }

        if (captureCase.ExpectedResponse is not { } expected)
        {
            return new CaptureReplayResult
            {
                Name = captureCase.Name,
                Outcome = CaptureReplayOutcome.NoExpectedResponse,
                FrameCount = frameCount,
                BytesFedOneAtATime = fed,
                ActualResponse = actual,
                Detail = $"'{captureCase.Name}{CaptureFixtureLoader.ExpectedExtension}' 가 없어 판정할 수 없습니다. "
                    + $"매뉴얼과 대조해 기대 응답을 작성하십시오. 현재 시뮬레이터 응답: {Hex.Format(actual)}",
            };
        }

        if (actual.AsSpan().SequenceEqual(expected))
        {
            return new CaptureReplayResult
            {
                Name = captureCase.Name,
                Outcome = CaptureReplayOutcome.Matched,
                FrameCount = frameCount,
                BytesFedOneAtATime = fed,
                ActualResponse = actual,
                Detail = $"{frameCount}프레임 일치 ({actual.Length}바이트)",
            };
        }

        return new CaptureReplayResult
        {
            Name = captureCase.Name,
            Outcome = CaptureReplayOutcome.Mismatched,
            FrameCount = frameCount,
            BytesFedOneAtATime = fed,
            ActualResponse = actual,
            Detail = $"응답 불일치.{Environment.NewLine}"
                + $"  기대: {Hex.Format(expected)}{Environment.NewLine}"
                + $"  실제: {Hex.Format(actual)}{Environment.NewLine}"
                + $"  {DescribeFirstDifference(expected, actual)}",
        };
    }

    /// <summary>여러 케이스를 재생한다.</summary>
    public IReadOnlyList<CaptureReplayResult> ReplayAll(IEnumerable<CaptureCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(Replay).ToArray();
    }

    private static string DescribeFirstDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        var shared = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < shared; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"첫 차이: 오프셋 {i} — 기대 0x{expected[i]:X2}, 실제 0x{actual[i]:X2}";
            }
        }

        return $"길이 차이: 기대 {expected.Length}바이트, 실제 {actual.Length}바이트";
    }
}
