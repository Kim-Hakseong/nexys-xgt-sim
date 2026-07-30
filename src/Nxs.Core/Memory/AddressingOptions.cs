namespace Nxs.Core.Memory;

/// <summary>
/// XGI 주소 산법 설정값. spec/xgi-addressing.md 확정 전까지 기본 가정값을 사용한다
/// (CLAUDE.md §4 — 확정 시 상수만 갱신, 산식 불변).
/// </summary>
public sealed record AddressingOptions
{
    /// <summary>슬롯당 고정 할당 점수. 기본 가정 64점.</summary>
    public int SlotPoints { get; init; } = 64;

    /// <summary>베이스당 슬롯 수. 미확정 — 기본 가정 12슬롯.</summary>
    public int SlotsPerBase { get; init; } = 12;

    /// <summary>베이스당 할당 점수 = <see cref="SlotsPerBase"/> × <see cref="SlotPoints"/>.</summary>
    public int BasePoints => SlotsPerBase * SlotPoints;

    /// <summary>기본값 인스턴스.</summary>
    public static AddressingOptions Default { get; } = new();

    /// <summary>
    /// 설정값이 산식에 사용 가능한지 검증한다. 슬롯/베이스 점수는 최대 크기 지정자(DWord=32비트)로
    /// 나누어떨어져야 슬롯 형식 주소를 워드/더블워드 단위로 환산할 수 있다.
    /// </summary>
    /// <exception cref="ArgumentException">설정값이 산식 전제를 위반할 때.</exception>
    public void Validate()
    {
        if (SlotPoints <= 0 || SlotPoints % 32 != 0)
        {
            throw new ArgumentException($"SlotPoints는 32의 양의 배수여야 합니다. 실제: {SlotPoints}", nameof(SlotPoints));
        }

        if (SlotsPerBase <= 0)
        {
            throw new ArgumentException($"SlotsPerBase는 양수여야 합니다. 실제: {SlotsPerBase}", nameof(SlotsPerBase));
        }
    }
}
