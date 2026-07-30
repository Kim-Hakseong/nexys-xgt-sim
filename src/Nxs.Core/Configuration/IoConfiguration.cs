using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>슬롯 하나의 구성. 모듈이 없으면 빈 슬롯.</summary>
public sealed record SlotConfig
{
    /// <summary>슬롯 번호.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>장착 모듈. null이면 빈 슬롯.</summary>
    public ModuleDefinition? Module { get; init; }
}

/// <summary>베이스 하나의 구성.</summary>
public sealed record BaseConfig
{
    /// <summary>베이스 번호.</summary>
    public required int BaseNumber { get; init; }

    /// <summary>슬롯 목록.</summary>
    public IReadOnlyList<SlotConfig> Slots { get; init; } = [];
}

/// <summary>
/// I/O 구성 모델 (PRD X-02). 베이스/슬롯/모듈 정의에서 메모리 범위를 자동 산출한다.
/// </summary>
public sealed record IoConfiguration
{
    /// <summary>주소 산법 설정. 슬롯 스트라이드를 정한다.</summary>
    public AddressingOptions Addressing { get; init; } = AddressingOptions.Default;

    /// <summary>베이스 목록.</summary>
    public IReadOnlyList<BaseConfig> Bases { get; init; } = [];

    /// <summary>
    /// CONTEXT.md 기재 랙을 만든다: XGI CPU / 슬롯0 FEnet / 슬롯1 Cnet /
    /// 슬롯2·3 DC입력32점 / 슬롯4 TR출력32점 / 슬롯5·6 A/D 16채널.
    /// </summary>
    /// <remarks>
    /// [결정] 이 랙의 슬롯 스트라이드는 256점(16워드)이다.
    /// 이유: XGF-AD16A 는 채널당 1워드로 16워드(=256비트)를 쓰므로 기본 가정 64점에는 담기지 않는다.
    /// DESIGN 의 산식은 그대로 두고 상수만 이 랙에 맞게 지정한다("spec 확정 시 상수만 갱신, 산식 불변").
    /// [미정] 실제 XGI 슬롯 할당 규칙은 spec/xgi-addressing.md 미기재 — 확정 시 이 상수만 바꾸면 된다.
    /// </remarks>
    public static IoConfiguration CreateDefaultRack() => new()
    {
        Addressing = new AddressingOptions { SlotPoints = 256, SlotsPerBase = 12 },
        Bases =
        [
            new BaseConfig
            {
                BaseNumber = 0,
                Slots =
                [
                    new SlotConfig { SlotNumber = 0, Module = ModuleCatalog.XglEfmtB },
                    new SlotConfig { SlotNumber = 1, Module = ModuleCatalog.XglC42A },
                    new SlotConfig { SlotNumber = 2, Module = ModuleCatalog.XgiD24A },
                    new SlotConfig { SlotNumber = 3, Module = ModuleCatalog.XgiD24A },
                    new SlotConfig { SlotNumber = 4, Module = ModuleCatalog.XgqTr4A },
                    new SlotConfig { SlotNumber = 5, Module = ModuleCatalog.XgfAd16A },
                    new SlotConfig { SlotNumber = 6, Module = ModuleCatalog.XgfAd16A },
                ],
            },
        ],
    };

    /// <summary>
    /// 구성에서 메모리 매핑을 산출한다. 통신 모듈과 빈 슬롯은 결과에 포함되지 않는다.
    /// </summary>
    /// <remarks>
    /// 시작 비트는 DESIGN 산식 그대로: <c>base × BasePoints + slot × SlotPoints</c>.
    /// </remarks>
    /// <exception cref="IoConfigurationException">구성이 주소 산법과 모순될 때.</exception>
    public IReadOnlyList<ModuleMapping> BuildMap()
    {
        Addressing.Validate();

        var result = new List<ModuleMapping>();
        var seenBases = new HashSet<int>();

        foreach (var baseConfig in Bases)
        {
            if (!seenBases.Add(baseConfig.BaseNumber))
            {
                throw new IoConfigurationException($"베이스 번호 {baseConfig.BaseNumber}가 중복되었습니다");
            }

            if (baseConfig.BaseNumber < 0)
            {
                throw new IoConfigurationException($"베이스 번호는 0 이상이어야 합니다: {baseConfig.BaseNumber}");
            }

            var seenSlots = new HashSet<int>();
            foreach (var slot in baseConfig.Slots)
            {
                if (!seenSlots.Add(slot.SlotNumber))
                {
                    throw new IoConfigurationException(
                        $"베이스 {baseConfig.BaseNumber}에서 슬롯 번호 {slot.SlotNumber}가 중복되었습니다");
                }

                if (slot.SlotNumber < 0 || slot.SlotNumber >= Addressing.SlotsPerBase)
                {
                    throw new IoConfigurationException(
                        $"슬롯 번호 {slot.SlotNumber}가 베이스 용량(0..{Addressing.SlotsPerBase - 1})을 벗어났습니다");
                }

                if (slot.Module is not { } module || module.Area is not { } area)
                {
                    continue;
                }

                if (module.OccupiedBits > Addressing.SlotPoints)
                {
                    throw new IoConfigurationException(
                        $"{module.ProductName}은 {module.OccupiedBits}비트를 쓰지만 슬롯 할당은 " +
                        $"{Addressing.SlotPoints}점입니다 — 슬롯당 점수를 늘리거나 모듈을 바꾸십시오");
                }

                var startBit = (baseConfig.BaseNumber * Addressing.BasePoints)
                    + (slot.SlotNumber * Addressing.SlotPoints);

                result.Add(new ModuleMapping
                {
                    BaseNumber = baseConfig.BaseNumber,
                    SlotNumber = slot.SlotNumber,
                    Module = module,
                    Area = area,
                    StartBit = startBit,
                    BitLength = module.OccupiedBits,
                });
            }
        }

        return result;
    }

    /// <summary>지정 슬롯의 매핑을 찾는다. 없으면 null.</summary>
    public ModuleMapping? FindMapping(int baseNumber, int slotNumber)
        => BuildMap().FirstOrDefault(m => m.BaseNumber == baseNumber && m.SlotNumber == slotNumber);
}
