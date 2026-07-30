namespace Nxs.Core.Memory;

/// <summary>PLC 메모리 크기 설정. DESIGN: 영역별 기본 64KB.</summary>
public sealed record PlcMemoryOptions
{
    /// <summary>영역별 바이트 크기. 기본 64KB.</summary>
    public int AreaSizeBytes { get; init; } = 64 * 1024;

    /// <summary>주소 산법 설정.</summary>
    public AddressingOptions Addressing { get; init; } = AddressingOptions.Default;

    /// <summary>기본값 인스턴스.</summary>
    public static PlcMemoryOptions Default { get; } = new();

    /// <summary>설정값을 검증한다.</summary>
    /// <exception cref="ArgumentException">크기가 4바이트 미만이거나 4의 배수가 아닐 때.</exception>
    public void Validate()
    {
        if (AreaSizeBytes < 4 || AreaSizeBytes % 4 != 0)
        {
            throw new ArgumentException(
                $"AreaSizeBytes는 4 이상의 4의 배수여야 합니다. 실제: {AreaSizeBytes}", nameof(AreaSizeBytes));
        }

        Addressing.Validate();
    }
}
