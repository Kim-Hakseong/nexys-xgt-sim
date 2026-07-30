using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 워치 목록 한 줄 — 사용자가 지정한 임의 주소(%MW320, %MD422 …)의 값을 보고 쓴다.
/// </summary>
public sealed partial class WatchRowViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private bool _updating;

    /// <summary>
    /// 현재 표시 중인 값. <see cref="Refresh"/>가 **외부 변경만** 반영하게 하는 기준이다
    /// (입력 중인 텍스트를 주기 갱신이 되돌리지 않도록).
    /// </summary>
    private uint _displayedValue;

    [ObservableProperty]
    private string _valueText = "0";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private WatchFormat _format;

    private readonly Action<WatchRowViewModel>? _onRemove;

    /// <summary>행을 만든다.</summary>
    /// <param name="memory">공유 PLC 메모리.</param>
    /// <param name="entry">워치 항목.</param>
    /// <param name="addressing">주소 산법 설정.</param>
    /// <param name="onRemove">
    /// 제거 요청 콜백. XAML 에서 조상 DataContext 를 캐스팅해 부모 커맨드를 호출하는 방식은
    /// 런타임 타입 해석에 실패할 수 있어(실제로 실패했다) 행 자신이 커맨드를 갖게 했다.
    /// </param>
    public WatchRowViewModel(
        PlcMemory memory,
        WatchEntry entry,
        AddressingOptions? addressing = null,
        Action<WatchRowViewModel>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(entry);

        _memory = memory;
        _onRemove = onRemove;
        Entry = entry;
        Address = entry.Resolve(addressing);
        _format = entry.Format;
        Show(ReadRaw());
    }

    /// <summary>원본 항목.</summary>
    public WatchEntry Entry { get; }

    /// <summary>해석된 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>사용자 별칭.</summary>
    public string Label => Entry.Label;

    /// <summary>크기 표기 (UI 배지).</summary>
    public string SizeText => Address.Size switch
    {
        DataSize.Bit => "BIT",
        DataSize.Byte => "BYTE",
        DataSize.Word => "WORD",
        DataSize.DWord => "DWORD",
        _ => "?",
    };

    /// <summary>
    /// 선택 가능한 표시 형식 목록 (UI 콤보박스).
    /// Avalonia 의 리플렉션 바인딩은 정적 프로퍼티를 인스턴스 경로로 찾지 못하므로 인스턴스로 노출한다.
    /// </summary>
    public IReadOnlyList<WatchFormat> Formats { get; } =
        [WatchFormat.Decimal, WatchFormat.Signed, WatchFormat.Hex, WatchFormat.Binary, WatchFormat.Bool];

    /// <summary>문서 생성 도구가 값 입력 후 형식을 일괄 적용할 때 쓰는 보관용 필드.</summary>
    public WatchFormat PendingFormat { get; set; } = WatchFormat.Decimal;

    /// <summary>이 행을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>현재 상태를 직렬화 형태로 만든다(프로젝트 저장용).</summary>
    public WatchEntry ToEntry() => Entry with { Format = Format };

    /// <summary>메모리가 외부에서 바뀐 경우에만 표시를 갱신한다.</summary>
    public void Refresh()
    {
        var raw = ReadRaw();
        if (raw == _displayedValue)
        {
            return;
        }

        Show(raw);
        Error = null;
    }

    private uint ReadRaw() => _memory.ReadScalar(Address);

    private void Show(uint value)
    {
        _updating = true;
        try
        {
            _displayedValue = value;
            ValueText = WatchEntry.Render(value, Address.Size, Format);
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnFormatChanged(WatchFormat value)
    {
        if (!_updating)
        {
            Show(_displayedValue);
        }
    }

    partial void OnValueTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        var parsed = WatchEntry.ParseInput(value, Address.Size);
        if (parsed is null)
        {
            Error = "값을 해석할 수 없습니다 (10진 / 0x16진 / ON·OFF)";
            return;
        }

        Error = null;
        _displayedValue = parsed.Value;
        _memory.WriteScalar(Address, parsed.Value);
    }
}
