using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nxs.Core.Configuration;

/// <summary>.nxp 파일이 이 프로그램이 읽을 수 있는 형식이 아닐 때 발생한다.</summary>
public sealed class NxpFormatException : Exception
{
    /// <summary>형식 예외를 만든다.</summary>
    public NxpFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>.nxp 프로젝트 파일 저장/로드 (PRD X-08).</summary>
public static class NxpProjectFile
{
    /// <summary>권장 확장자.</summary>
    public const string Extension = ".nxp";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>프로젝트를 저장한다.</summary>
    /// <remarks>
    /// 임시 파일에 쓴 뒤 교체하므로, 저장이 실패해도 기존 파일이 손상되지 않는다.
    /// 저장 전에 구성을 검증한다 — 열 수 없는 프로젝트를 만들어 두지 않기 위해서다.
    /// </remarks>
    /// <exception cref="IoConfigurationException">I/O 구성이 주소 산법과 모순될 때.</exception>
    /// <exception cref="FormatException">서버 설정을 해석할 수 없을 때.</exception>
    public static void Save(string path, NxpProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);

        // 저장 전 검증: 매핑이 성립하고 서버 설정과 워치 주소가 유효해야 한다.
        project.Io.BuildMap();
        project.Server.ToServerOptions();
        foreach (var watch in project.Watches)
        {
            watch.Resolve(project.Io.Addressing);
        }

        var json = JsonSerializer.Serialize(project, Options);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>프로젝트를 로드한다.</summary>
    /// <exception cref="NxpFormatException">JSON이 깨졌거나 포맷 버전을 지원하지 않을 때.</exception>
    public static NxpProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = File.ReadAllText(path);

        // 스키마 바인딩 전에 버전을 먼저 확인한다. 미래 버전 파일은 이 프로그램이 모르는 필드를
        // 가질 수 있으므로, 바인딩 실패를 "JSON 오류"로 오진하지 않고 버전 문제로 정확히 보고한다.
        ValidateFormatVersion(path, json);

        NxpProject? project;
        try
        {
            project = JsonSerializer.Deserialize<NxpProject>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new NxpFormatException($"'{path}'를 읽을 수 없습니다: JSON 형식 오류 — {ex.Message}", ex);
        }

        if (project is null)
        {
            throw new NxpFormatException($"'{path}'가 비어 있습니다");
        }

        return project;
    }

    private static void ValidateFormatVersion(string path, string json)
    {
        int version;
        try
        {
            using var document = JsonDocument.Parse(json);
            version = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("formatVersion", out var element)
                && element.TryGetInt32(out var parsed)
                    ? parsed
                    : NxpProject.CurrentFormatVersion;
        }
        catch (JsonException ex)
        {
            throw new NxpFormatException($"'{path}'를 읽을 수 없습니다: JSON 형식 오류 — {ex.Message}", ex);
        }

        if (version > NxpProject.CurrentFormatVersion)
        {
            throw new NxpFormatException(
                $"지원하지 않는 formatVersion {version}입니다 " +
                $"(이 프로그램은 {NxpProject.CurrentFormatVersion}까지 지원). 최신 버전으로 여십시오.");
        }

        if (version < 1)
        {
            throw new NxpFormatException($"formatVersion {version}은 올바르지 않습니다");
        }
    }
}
