using System.Globalization;

namespace Nxs.Core.Memory;

/// <summary>
/// 주소 묶음 하나 — 여기 속한 주소들은 항상 같은 값을 갖는다.
/// </summary>
/// <remarks>
/// <para>
/// 실장비에서는 PLC 프로그램이 "MW0 을 MW1 에 복사" 같은 로직을 돌리는데, 시뮬레이터에는
/// 그 프로그램이 없다. 묶음은 그 자리를 메운다 — 한쪽에 값이 들어가면 나머지가 따라온다.
/// </para>
/// <para>
/// 방향이 없다(대칭). 어느 쪽에 써도 나머지로 퍼진다 — "묶는다"는 말에 방향이 없기 때문이다.
/// </para>
/// </remarks>
public sealed record MemoryLinkGroup
{
    /// <summary>묶인 주소 표기들. 모두 같은 크기여야 한다.</summary>
    public required IReadOnlyList<string> Addresses { get; init; }

    /// <summary>사용자 별칭(무엇을 묶었는지).</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>해석이 끝난 묶음 — 주소가 IecAddress 로 확정되어 있다.</summary>
public sealed class ResolvedLinkGroup
{
    /// <summary>묶음을 만든다.</summary>
    /// <exception cref="ArgumentException">멤버가 2개 미만이거나 크기가 서로 다를 때.</exception>
    public ResolvedLinkGroup(IReadOnlyList<IecAddress> members, string label = "")
    {
        ArgumentNullException.ThrowIfNull(members);

        if (members.Count < 2)
        {
            throw new ArgumentException("묶음에는 주소가 2개 이상 있어야 합니다", nameof(members));
        }

        // 크기가 다르면 무엇을 무엇에 복사할지 정할 수 없다 — 조용히 자르지 않고 거절한다.
        var size = members[0].Size;
        foreach (var member in members)
        {
            if (member.Size != size)
            {
                throw new ArgumentException(
                    $"묶음의 주소는 크기가 같아야 합니다 — {members[0].Text}({size})와 "
                    + $"{member.Text}({member.Size}) 가 다릅니다",
                    nameof(members));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (!seen.Add(member.Text))
            {
                throw new ArgumentException($"묶음에 {member.Text} 가 두 번 들어 있습니다", nameof(members));
            }
        }

        Members = members;
        Label = label;
    }

    /// <summary>묶인 주소들.</summary>
    public IReadOnlyList<IecAddress> Members { get; }

    /// <summary>사용자 별칭.</summary>
    public string Label { get; }

    /// <summary>멤버 크기.</summary>
    public DataSize Size => Members[0].Size;

    /// <summary>사람이 읽는 표기 — <c>%MW0 = %MW1</c>.</summary>
    public string Text => string.Join(" = ", Members.Select(m => m.Text));

    /// <summary>이 묶음을 직렬화 형태로 되돌린다.</summary>
    public MemoryLinkGroup ToEntry()
        => new() { Addresses = Members.Select(m => m.Text).ToArray(), Label = Label };
}

/// <summary>
/// 주소 묶음 모음. 쓰기가 일어난 자리와 겹치는 묶음을 찾아 값을 퍼뜨린다.
/// </summary>
/// <remarks>
/// <para>
/// 겹침 판정은 **비트 단위**로 한다. 바이트 단위로 하면 같은 워드 안의 서로 다른 비트
/// (<c>%MW0</c> 의 10번 비트와 12번 비트)를 구분할 수 없어, 10번 비트에 쓴 것이 12번 비트에
/// 쓴 것으로 오인된다.
/// </para>
/// <para>
/// 한 번의 쓰기가 같은 묶음의 멤버를 여러 개 덮으면(예: 연속 쓰기가 %MW0·%MW1 을 함께 덮음)
/// **가장 낮은 번지**를 원본으로 삼는다. 임의로 정한 규칙이 아니라 정해 두어야 하는 규칙이다 —
/// 정하지 않으면 같은 프레임에 대해 결과가 달라진다.
/// </para>
/// </remarks>
public sealed class MemoryLinks
{
    /// <summary>한 프로젝트가 가질 수 있는 묶음 수 상한.</summary>
    public const int MaxGroups = 512;

    private readonly ResolvedLinkGroup[] _groups;

    /// <summary>묶음 모음을 만든다.</summary>
    /// <exception cref="ArgumentOutOfRangeException">묶음이 상한을 넘을 때.</exception>
    public MemoryLinks(IReadOnlyList<ResolvedLinkGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (groups.Count > MaxGroups)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groups), groups.Count, $"묶음은 {MaxGroups}개까지입니다");
        }

        _groups = [.. groups];
    }

    /// <summary>묶음이 하나도 없는 모음.</summary>
    public static MemoryLinks Empty { get; } = new([]);

    /// <summary>묶음들.</summary>
    public IReadOnlyList<ResolvedLinkGroup> Groups => _groups;

    /// <summary>묶음이 하나도 없는지 — 쓰기 경로가 빨리 빠져나가는 데 쓴다.</summary>
    public bool IsEmpty => _groups.Length == 0;

    /// <summary>주소의 비트 범위 — [시작 비트, 끝 비트).</summary>
    public static (int Start, int End) BitExtent(IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Size == DataSize.Bit
            ? (address.Offset, address.Offset + 1)
            : (address.ByteStart * 8, address.ByteEnd * 8);
    }

    /// <summary>
    /// 쓰기가 닿은 비트 범위와 겹치는 묶음들을, 각 묶음의 원본 주소와 함께 돌려준다.
    /// </summary>
    public IEnumerable<(ResolvedLinkGroup Group, IecAddress Source)> Affected(
        MemoryArea area, int startBit, int bitCount)
    {
        if (_groups.Length == 0 || bitCount <= 0)
        {
            yield break;
        }

        var endBit = startBit + bitCount;

        foreach (var group in _groups)
        {
            IecAddress? source = null;
            foreach (var member in group.Members)
            {
                if (member.Area != area)
                {
                    continue;
                }

                var (memberStart, memberEnd) = BitExtent(member);
                if (memberStart >= endBit || memberEnd <= startBit)
                {
                    continue;
                }

                // 여러 멤버가 겹치면 가장 낮은 번지가 원본이다.
                if (source is null || memberStart < BitExtent(source).Start)
                {
                    source = member;
                }
            }

            if (source is not null)
            {
                yield return (group, source);
            }
        }
    }

    /// <summary>사람이 읽는 요약.</summary>
    public string Describe()
        => _groups.Length == 0
            ? "묶음 없음"
            : $"{_groups.Length.ToString(CultureInfo.InvariantCulture)}개 묶음";
}
