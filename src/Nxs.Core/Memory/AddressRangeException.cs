namespace Nxs.Core.Memory;

/// <summary>
/// 요청한 주소/길이가 영역 경계를 벗어났을 때 발생한다.
/// 프로토콜 계층이 실장비와 동일한 에러 응답으로 변환하는 지점 (PRD X-04).
/// </summary>
public sealed class AddressRangeException : Exception
{
    /// <summary>대상 영역.</summary>
    public MemoryArea Area { get; }

    /// <summary>요청 시작 바이트.</summary>
    public int ByteStart { get; }

    /// <summary>요청 바이트 길이.</summary>
    public int ByteLength { get; }

    /// <summary>영역의 바이트 크기.</summary>
    public int AreaSizeBytes { get; }

    /// <summary>범위 위반 예외를 만든다.</summary>
    public AddressRangeException(MemoryArea area, int byteStart, int byteLength, int areaSizeBytes)
        : base($"%{area} 영역 범위 초과: 요청 [{byteStart}, {byteStart + byteLength}) / 영역 크기 {areaSizeBytes}바이트")
    {
        Area = area;
        ByteStart = byteStart;
        ByteLength = byteLength;
        AreaSizeBytes = areaSizeBytes;
    }
}
