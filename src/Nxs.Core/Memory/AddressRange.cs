using System.Globalization;

namespace Nxs.Core.Memory;

/// <summary>
/// 시작 주소 + 개수를 연속된 주소 목록으로 펼친다.
/// </summary>
/// <remarks>
/// <para>
/// 마스터가 어느 주소를 건드리는지 모를 때 하나씩 추가해 보는 것은 현실적이지 않다 —
/// %MW0 부터 100개를 한 번에 펼쳐 놓고 값이 움직이는 곳을 찾는 편이 빠르다.
/// </para>
/// <para>
/// 증분은 **표기 단위**다. <c>%MW100</c> 에서 5개면 <c>%MW100 … %MW104</c> 이고,
/// <c>%MX0</c> 에서 5개면 비트 <c>%MX0 … %MX4</c> 다. 바이트로 환산하지 않는다 —
/// 사용자가 화면에서 읽는 번지와 목록의 번지가 같아야 헷갈리지 않는다.
/// </para>
/// </remarks>
public static class AddressRange
{
    /// <summary>한 번에 펼칠 수 있는 최대 개수.</summary>
    /// <remarks>
    /// 상한이 없으면 오타 하나(1000000)로 UI 가 멈춘다. 상한에 걸리면 조용히 자르지 않고 알린다.
    /// </remarks>
    public const int MaxCount = 4096;

    /// <summary>범위를 펼친다.</summary>
    /// <param name="startAddress">시작 주소 표기(<c>%MW100</c> 등).</param>
    /// <param name="count">개수.</param>
    /// <param name="addressing">주소 산법 설정.</param>
    /// <param name="memory">메모리 범위 확인용. null 이면 범위를 검사하지 않는다.</param>
    /// <returns>펼친 주소 목록.</returns>
    /// <exception cref="FormatException">시작 주소를 해석할 수 없을 때.</exception>
    /// <exception cref="ArgumentOutOfRangeException">개수가 1 미만이거나 <see cref="MaxCount"/> 초과일 때.</exception>
    /// <exception cref="InvalidOperationException">범위가 메모리 끝을 넘을 때.</exception>
    public static IReadOnlyList<IecAddress> Expand(
        string startAddress,
        int count,
        AddressingOptions? addressing = null,
        PlcMemory? memory = null)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "개수는 1 이상이어야 합니다");
        }

        if (count > MaxCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"한 번에 펼칠 수 있는 개수는 {MaxCount}개까지입니다");
        }

        var options = addressing ?? AddressingOptions.Default;
        var start = IecAddress.Parse(startAddress, options);

        var list = new List<IecAddress>(count);
        for (var i = 0; i < count; i++)
        {
            var address = IecAddress.Parse(Format(start, start.Offset + i), options);

            if (memory is not null && address.ByteEnd > memory.AreaSizeBytes)
            {
                throw new InvalidOperationException(
                    $"{start.Text} 에서 {count}개는 메모리 끝을 넘습니다 — "
                    + $"{address.Text} 가 영역 크기 {memory.AreaSizeBytes}바이트를 벗어납니다");
            }

            list.Add(address);
        }

        return list;
    }

    /// <summary>범위를 펼칠 수 있는지 미리 확인한다(UI 가 버튼을 막을 때 쓴다).</summary>
    public static bool CanExpand(string? startAddress, int count)
        => count >= 1
            && count <= MaxCount
            && !string.IsNullOrWhiteSpace(startAddress)
            && IecAddress.TryParse(startAddress, out _);

    /// <summary>같은 영역·크기의 다른 번지 표기를 만든다.</summary>
    private static string Format(IecAddress template, int offset)
        => $"%{AreaChar(template.Area)}{SizeChar(template.Size)}"
            + offset.ToString(CultureInfo.InvariantCulture);

    private static char AreaChar(MemoryArea area) => area switch
    {
        MemoryArea.I => 'I',
        MemoryArea.Q => 'Q',
        MemoryArea.M => 'M',
        _ => throw new InvalidOperationException($"알 수 없는 영역: {area}"),
    };

    private static char SizeChar(DataSize size) => size switch
    {
        DataSize.Bit => 'X',
        DataSize.Byte => 'B',
        DataSize.Word => 'W',
        DataSize.DWord => 'D',
        DataSize.LWord => 'L',
        _ => throw new InvalidOperationException($"알 수 없는 크기: {size}"),
    };
}
