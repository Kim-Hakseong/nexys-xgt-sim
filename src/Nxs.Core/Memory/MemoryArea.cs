namespace Nxs.Core.Memory;

/// <summary>XGI 메모리 영역. IEC 표기 %I / %Q / %M 에 대응한다.</summary>
public enum MemoryArea
{
    /// <summary>입력 영역 %I (모듈 → CPU).</summary>
    I,

    /// <summary>출력 영역 %Q (CPU → 모듈).</summary>
    Q,

    /// <summary>내부 메모리 영역 %M.</summary>
    M,
}
