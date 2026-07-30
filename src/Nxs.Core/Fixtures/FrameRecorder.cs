using Nxs.Core.Protocol;

namespace Nxs.Core.Fixtures;

/// <summary>
/// 수신 요청 프레임을 픽스처 디렉터리에 자동 저장한다.
/// </summary>
/// <remarks>
/// <para>
/// **왜 필요한가** — 완성된 LabVIEW 코드는 이미 실장비가 읽을 수 있는 정답 프레임을 만든다.
/// 그 프레임이 이 프로젝트에서 가장 확실한 검증 근거인데, 사람이 <c>nc -l</c> 로 따로 캡처하려면
/// 절차가 하나 더 늘어난다. 시뮬레이터가 접속을 받을 때 알아서 기록하면 **검증 루프가 스스로 닫힌다**:
/// LabVIEW 를 한 번 붙이는 것만으로 초안 검증용 근거가 모인다.
/// </para>
/// <para>
/// 같은 모양의 프레임(명령·데이터타입·주소 조합)은 한 번만 저장한다 — 폴링으로 수천 개가 쌓이면
/// 쓸모가 없다. 응답도 같이 저장하되 <c>.actual</c> 확장자를 쓴다:
/// <c>.expected</c> 는 **사람이 매뉴얼과 대조해 확정한 것만** 들어가야 하기 때문이다.
/// </para>
/// <para>스레드 안전. 저장 실패는 통신을 막지 않는다(조용히 건너뛰고 카운터만 올린다).</para>
/// </remarks>
public sealed class FrameRecorder
{
    private readonly object _gate = new();
    private readonly HashSet<string> _seenShapes = [];
    private readonly string _directory;
    private readonly int _maxFiles;
    private int _savedCount;
    private int _failedCount;

    /// <summary>기록기를 만든다.</summary>
    /// <param name="directory">저장 디렉터리. 없으면 만든다.</param>
    /// <param name="maxFiles">저장할 최대 프레임 모양 수.</param>
    public FrameRecorder(string directory, int maxFiles = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maxFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles), maxFiles, "1 이상이어야 합니다");
        }

        _directory = directory;
        _maxFiles = maxFiles;
    }

    /// <summary>저장된 프레임 모양 수.</summary>
    public int SavedCount
    {
        get
        {
            lock (_gate)
            {
                return _savedCount;
            }
        }
    }

    /// <summary>저장 실패 횟수.</summary>
    public int FailedCount
    {
        get
        {
            lock (_gate)
            {
                return _failedCount;
            }
        }
    }

    /// <summary>
    /// 요청/응답 쌍을 기록한다. 이미 같은 모양을 저장했거나 한계에 도달하면 아무것도 하지 않는다.
    /// </summary>
    /// <param name="requestFrame">수신한 요청 프레임 전체.</param>
    /// <param name="responseFrame">보낸 응답 프레임 전체(없으면 빈 배열).</param>
    /// <param name="shapeKey">
    /// 프레임 모양 식별자. 보통 코덱의 요청 요약을 쓴다(같은 주소·명령이면 같은 문자열).
    /// </param>
    /// <returns>이번 호출로 새로 저장했으면 참.</returns>
    public bool Record(ReadOnlySpan<byte> requestFrame, ReadOnlySpan<byte> responseFrame, string shapeKey)
    {
        string name;
        lock (_gate)
        {
            if (_savedCount >= _maxFiles || !_seenShapes.Add(shapeKey))
            {
                return false;
            }

            name = $"req_{_savedCount:D2}_{Sanitize(shapeKey)}";
            _savedCount++;
        }

        var request = requestFrame.ToArray();
        var response = responseFrame.ToArray();

        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Path.Combine(_directory, name + CaptureFixtureLoader.RequestExtension), request);

            // .actual — 시뮬레이터가 실제로 낸 응답. .expected 가 아니다(사람 확정본만 .expected).
            if (response.Length > 0)
            {
                File.WriteAllBytes(Path.Combine(_directory, name + ".actual"), response);
            }

            File.WriteAllText(
                Path.Combine(_directory, name + ".txt"),
                $"""
                # 자동 캡처 — 시뮬레이터가 수신한 실제 마스터 요청
                # 모양: {shapeKey}
                #
                # 요청 hex ({request.Length}바이트)
                {Hex.Format(request)}
                #
                # 시뮬레이터 응답 hex ({response.Length}바이트) — **기대값이 아니라 현재 구현의 출력**
                {Hex.Format(response)}
                #
                # 검증하려면: spec/xgt-fenet-reference.md §8 절차로 위 요청 hex 의 헤더를 대조하고,
                # 매뉴얼로 확정한 기대 응답을 {name}.expected 로 저장하십시오.
                # (.actual 을 .expected 로 복사하면 회귀가 자기 자신을 검증하게 되어 무의미합니다)

                """);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _failedCount++;
            }

            return false;
        }
    }

    private static string Sanitize(string text)
    {
        var chars = text.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').Take(40).ToArray();
        return new string(chars).Trim('_');
    }
}
