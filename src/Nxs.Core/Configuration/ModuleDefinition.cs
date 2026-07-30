namespace Nxs.Core.Configuration;

/// <summary>모듈 종류.</summary>
public enum ModuleKind
{
    /// <summary>통신 모듈(FEnet/Cnet) — 프로세스 데이터 영역을 쓰지 않는다.</summary>
    Communication,

    /// <summary>디지털 입력 — %I 비트.</summary>
    DigitalInput,

    /// <summary>디지털 출력 — %Q 비트.</summary>
    DigitalOutput,

    /// <summary>아날로그 입력 — %I 워드(채널당 1워드).</summary>
    AnalogInput,
}

/// <summary>모듈 정의. 슬롯에 장착되어 메모리 영역을 차지한다.</summary>
public sealed record ModuleDefinition
{
    /// <summary>제품명. 예: <c>XGI-D24A</c>.</summary>
    public required string ProductName { get; init; }

    /// <summary>모듈 종류.</summary>
    public required ModuleKind Kind { get; init; }

    /// <summary>디지털 점수. 디지털 모듈에만 유효.</summary>
    public int PointCount { get; init; }

    /// <summary>아날로그 채널 수. 아날로그 모듈에만 유효.</summary>
    public int ChannelCount { get; init; }

    /// <summary>설명(UI 표시용).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>이 모듈이 차지하는 비트 수. 통신 모듈은 0.</summary>
    public int OccupiedBits => Kind switch
    {
        ModuleKind.DigitalInput or ModuleKind.DigitalOutput => PointCount,
        ModuleKind.AnalogInput => ChannelCount * 16,
        ModuleKind.Communication => 0,
        _ => 0,
    };

    /// <summary>이 모듈의 데이터가 놓이는 영역. 통신 모듈은 null.</summary>
    public Memory.MemoryArea? Area => Kind switch
    {
        ModuleKind.DigitalInput or ModuleKind.AnalogInput => Memory.MemoryArea.I,
        ModuleKind.DigitalOutput => Memory.MemoryArea.Q,
        _ => null,
    };

    /// <summary>UI·주소 표기에서 자연스러운 접근 단위.</summary>
    public Memory.DataSize PreferredView => Kind == ModuleKind.AnalogInput
        ? Memory.DataSize.Word
        : Memory.DataSize.Bit;
}

/// <summary>
/// CONTEXT.md 기재 랙 구성의 모듈 카탈로그.
/// </summary>
/// <remarks>
/// 점수·채널 수는 CONTEXT.md 에 기재된 값이다(XGI-D24A DC입력 32점, XGQ-TR4A TR출력 32점,
/// XGF-AD16A A/D 16채널). 슬롯 할당 점수는 <see cref="Memory.AddressingOptions"/> 소관이다.
/// </remarks>
public static class ModuleCatalog
{
    /// <summary>슬롯0 — FEnet 통신 모듈.</summary>
    public static ModuleDefinition XglEfmtB { get; } = new()
    {
        ProductName = "XGL-EFMT(B)",
        Kind = ModuleKind.Communication,
        Description = "FEnet 이더넷 통신",
    };

    /// <summary>슬롯1 — Cnet 통신 모듈.</summary>
    public static ModuleDefinition XglC42A { get; } = new()
    {
        ProductName = "XGL-C42A",
        Kind = ModuleKind.Communication,
        Description = "Cnet 시리얼 통신 (RS-422/485)",
    };

    /// <summary>DC 입력 32점.</summary>
    public static ModuleDefinition XgiD24A { get; } = new()
    {
        ProductName = "XGI-D24A",
        Kind = ModuleKind.DigitalInput,
        PointCount = 32,
        Description = "DC 입력 32점",
    };

    /// <summary>TR 출력 32점.</summary>
    public static ModuleDefinition XgqTr4A { get; } = new()
    {
        ProductName = "XGQ-TR4A",
        Kind = ModuleKind.DigitalOutput,
        PointCount = 32,
        Description = "TR 출력 32점",
    };

    /// <summary>A/D 입력 16채널.</summary>
    public static ModuleDefinition XgfAd16A { get; } = new()
    {
        ProductName = "XGF-AD16A",
        Kind = ModuleKind.AnalogInput,
        ChannelCount = 16,
        Description = "A/D 입력 16채널",
    };

    /// <summary>카탈로그 전체.</summary>
    public static IReadOnlyList<ModuleDefinition> All { get; } =
        [XglEfmtB, XglC42A, XgiD24A, XgqTr4A, XgfAd16A];

    /// <summary>제품명으로 모듈을 찾는다.</summary>
    public static ModuleDefinition? Find(string? productName)
        => All.FirstOrDefault(m => string.Equals(m.ProductName, productName, StringComparison.OrdinalIgnoreCase));
}
