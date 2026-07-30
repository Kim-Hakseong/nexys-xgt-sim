namespace Nxs.Core.Fixtures;

/// <summary>
/// 캡처 픽스처 디렉터리를 읽는다 (README_KIT 절차 — LabVIEW 요청을 더미 리스너로 캡처해 저장).
/// </summary>
/// <remarks>
/// 규약: <c>&lt;name&gt;.bin</c> = 캡처된 요청 바이트, 같은 이름의 <c>&lt;name&gt;.expected</c> = 기대 응답.
/// <c>.expected</c> 는 사람이 매뉴얼과 대조해 확정한다 — 시뮬레이터 출력을 그대로 복사하면
/// 회귀 테스트가 자기 자신을 검증하는 셈이 되어 의미가 없다.
/// </remarks>
public static class CaptureFixtureLoader
{
    /// <summary>기본 픽스처 디렉터리 상대 경로.</summary>
    public const string DefaultRelativePath = "fixtures/labview-capture";

    /// <summary>요청 파일 확장자.</summary>
    public const string RequestExtension = ".bin";

    /// <summary>기대 응답 파일 확장자.</summary>
    public const string ExpectedExtension = ".expected";

    /// <summary>
    /// 디렉터리에서 케이스를 읽는다. 디렉터리가 없거나 비어 있으면 빈 목록을 반환한다
    /// (부재는 오류가 아니다 — DESIGN "부재 시 skip").
    /// </summary>
    public static IReadOnlyList<CaptureCase> Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var cases = new List<CaptureCase>();
        foreach (var requestPath in Directory.EnumerateFiles(directory, $"*{RequestExtension}"))
        {
            var name = Path.GetFileNameWithoutExtension(requestPath);
            var expectedPath = Path.Combine(directory, name + ExpectedExtension);

            cases.Add(new CaptureCase(
                name,
                File.ReadAllBytes(requestPath),
                File.Exists(expectedPath) ? File.ReadAllBytes(expectedPath) : null));
        }

        // 결정적 순서 — 파일시스템 열거 순서에 의존하지 않는다.
        cases.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return cases;
    }

    /// <summary>
    /// 시작 디렉터리에서 위로 올라가며 픽스처 디렉터리를 찾는다(테스트가 bin/ 하위에서 실행되기 때문).
    /// </summary>
    /// <returns>찾은 절대 경로, 없으면 null.</returns>
    public static string? FindDirectory(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, DefaultRelativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
