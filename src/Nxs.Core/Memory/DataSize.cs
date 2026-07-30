namespace Nxs.Core.Memory;

/// <summary>IEC 주소의 크기 지정자. %..X / %..B / %..W / %..D.</summary>
public enum DataSize
{
    /// <summary>비트 (X).</summary>
    Bit,

    /// <summary>바이트 (B).</summary>
    Byte,

    /// <summary>워드 = 2바이트 (W).</summary>
    Word,

    /// <summary>더블워드 = 4바이트 (D).</summary>
    DWord,

    /// <summary>롱워드 = 8바이트 (L). Double 값과 XGT 데이터 타입 0x0004 에 대응한다.</summary>
    LWord,
}

/// <summary>크기 지정자별 비트 폭 헬퍼.</summary>
public static class DataSizeExtensions
{
    /// <summary>지정자 1단위의 비트 수를 반환한다.</summary>
    public static int BitWidth(this DataSize size) => size switch
    {
        DataSize.Bit => 1,
        DataSize.Byte => 8,
        DataSize.Word => 16,
        DataSize.DWord => 32,
        DataSize.LWord => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "알 수 없는 크기 지정자"),
    };

    /// <summary>IEC 표기 문자('X','B','W','D')를 반환한다.</summary>
    public static char Letter(this DataSize size) => size switch
    {
        DataSize.Bit => 'X',
        DataSize.Byte => 'B',
        DataSize.Word => 'W',
        DataSize.DWord => 'D',
        DataSize.LWord => 'L',
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "알 수 없는 크기 지정자"),
    };
}
