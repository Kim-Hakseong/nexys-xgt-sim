namespace Nxs.Core.Automation;

/// <summary>제너레이터 종류(직렬화·UI 선택용).</summary>
public enum GeneratorKind
{
    /// <summary>고정.</summary>
    Fixed,

    /// <summary>증가(모듈러 카운터).</summary>
    Increment,

    /// <summary>램프(최대에 도달한 뒤 최소로 복귀).</summary>
    Ramp,

    /// <summary>사인.</summary>
    Sine,

    /// <summary>랜덤(시드 고정 → 재현 가능).</summary>
    Random,

    /// <summary>토글.</summary>
    Toggle,
}

/// <summary>
/// 값 제너레이터. <see cref="ValueAt"/>는 tick 인덱스의 **순수 함수**여야 한다
/// (DESIGN — 상태를 갖지 않아 임의 순서 호출에도 같은 값이 나온다).
/// </summary>
public interface IValueGenerator
{
    /// <summary>제너레이터 종류.</summary>
    GeneratorKind Kind { get; }

    /// <summary>tick 인덱스에 해당하는 값.</summary>
    /// <param name="tickIndex">0부터 시작하는 tick 번호.</param>
    /// <exception cref="ArgumentOutOfRangeException">tick 이 음수일 때.</exception>
    /// <exception cref="ArgumentException">파라미터가 유효하지 않을 때.</exception>
    int ValueAt(int tickIndex);
}
