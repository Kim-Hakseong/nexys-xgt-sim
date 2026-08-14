using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;
using Nxs.Core.Time;

namespace Nxs.App.ViewModels;

/// <summary>
/// 범위 보기의 칸 하나 — 주소 하나의 현재 값과 **최근에 바뀌었는지**를 보여준다.
/// </summary>
/// <remarks>
/// 최근 변경 표시가 이 화면의 핵심이다. 마스터가 어느 주소를 건드리는지 모를 때
/// 값만 늘어놓으면 여전히 눈으로 훑어야 하지만, 방금 바뀐 칸이 물들면 바로 눈에 띈다.
/// </remarks>
public sealed partial class RangeCellViewModel : ObservableObject
{
    /// <summary>변경 표시가 남아 있는 시간.</summary>
    /// <remarks>
    /// 너무 짧으면 눈을 돌린 사이에 사라지고, 너무 길면 화면이 통째로 물들어 구분이 안 된다.
    /// </remarks>
    public static readonly TimeSpan ChangeHighlight = TimeSpan.FromSeconds(3);

    private readonly PlcMemory _memory;
    private readonly ITimeSource _time;
    private byte[] _displayed = [];
    private DateTimeOffset _lastChangeUtc = DateTimeOffset.MinValue;

    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    private bool _isRecentlyChanged;

    /// <summary>칸을 만든다.</summary>
    public RangeCellViewModel(
        PlcMemory memory,
        IecAddress address,
        WatchFormat format,
        ByteOrder order,
        ITimeSource time)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(time);

        _memory = memory;
        _time = time;
        Address = address;
        Format = format;
        Order = order;

        _displayed = memory.ReadRaw(address);
        ValueText = WatchValue.Render(_displayed, format, order);
    }

    /// <summary>이 칸의 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>표시 형식.</summary>
    public WatchFormat Format { get; }

    /// <summary>바이트 순서.</summary>
    public ByteOrder Order { get; }

    /// <summary>
    /// 칸 너비(px). 형식·폭이 만들 수 있는 **가장 긴 표기**에 맞춰 정한다.
    /// </summary>
    /// <remarks>
    /// 고정 너비로 두면 2진 워드(<c>0000 0100 1101 0010</c>)처럼 긴 표기가 잘린다.
    /// 값에 따라 매번 재는 방식은 값이 바뀔 때마다 칸이 들썩여 읽기 어렵다 —
    /// 그래서 값이 아니라 **형식이 낼 수 있는 최대 길이**로 한 번만 정한다.
    /// </remarks>
    public double CellWidth => WidthFor(Format, Address);

    /// <summary>칸 너비를 계산한다(형식·주소 폭이 같으면 같은 값).</summary>
    public static double WidthFor(WatchFormat format, IecAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // 글꼴을 재지 않고 글자 수로 정한다 — 결정적이고 테스트할 수 있다.
        const double ValueCharWidth = 7.4;    // Consolas 12px
        const double AddressCharWidth = 6.2;  // Consolas 10px
        const double Padding = 20;

        var valueChars = WatchValue.MaxRenderedLength(format, address.ByteLength);
        var width = Math.Max(valueChars * ValueCharWidth, address.Text.Length * AddressCharWidth);
        // 상한은 최악의 경우(LWORD 2진 = 79글자)도 담을 만큼 둔다 — 자르면 화면이 쓸모없어진다.
        return Math.Clamp(Math.Ceiling(width + Padding), 96, 640);
    }

    /// <summary>메모리를 다시 읽어 표시를 맞춘다.</summary>
    /// <remarks>
    /// 값이 그대로면 아무 속성도 건드리지 않는다 — 칸이 수천 개라 무조건 알림을 내면 UI 가 버겁다.
    /// 변경 표시만 시간이 지나면 스스로 꺼진다.
    /// </remarks>
    public void Refresh()
    {
        var raw = _memory.ReadRaw(Address);

        if (!raw.AsSpan().SequenceEqual(_displayed))
        {
            _displayed = raw;
            ValueText = WatchValue.Render(raw, Format, Order);
            _lastChangeUtc = _time.UtcNow;
            IsRecentlyChanged = true;
            return;
        }

        if (IsRecentlyChanged && _time.UtcNow - _lastChangeUtc >= ChangeHighlight)
        {
            IsRecentlyChanged = false;
        }
    }
}
