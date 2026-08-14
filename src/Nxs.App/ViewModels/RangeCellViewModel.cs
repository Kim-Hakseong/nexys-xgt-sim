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
