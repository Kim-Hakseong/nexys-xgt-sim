using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 워치 목록 한 줄 — 사용자가 지정한 임의 주소(%MW320, %MD422, %ML50 …)의 값을 보고 쓴다.
/// </summary>
/// <remarks>
/// 값을 <c>uint</c> 로 바꾸지 않고 **메모리 바이트를 직접** 다룬다. 엔디안(워드오더)이
/// 바이트 순서 문제이고, Double 은 8바이트라 32비트에 담기지 않기 때문이다.
/// </remarks>
public sealed partial class WatchRowViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly Action<WatchRowViewModel>? _onRemove;
    private bool _updating;

    /// <summary>
    /// 현재 표시 중인 바이트. <see cref="Refresh"/>가 **외부 변경만** 반영하게 하는 기준이다
    /// (주기 갱신이 입력 중인 텍스트를 되돌리지 않도록).
    /// </summary>
    private byte[] _displayed = [];

    [ObservableProperty]
    private string _valueText = "0";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private WatchFormat _format;

    [ObservableProperty]
    private ByteOrder _order;

    [ObservableProperty]
    private DisplayOption<WatchFormat> _selectedFormatOption = null!;

    [ObservableProperty]
    private DisplayOption<ByteOrder>? _selectedOrderOption;

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
        _order = entry.Order;

        // 폭에 맞는 형식만 노출한다 — 2바이트 주소에 Double 을 고를 수 있으면 혼란만 준다.
        Formats = new[]
        {
            WatchFormat.Decimal, WatchFormat.Signed, WatchFormat.Hex,
            WatchFormat.Binary, WatchFormat.Bool, WatchFormat.Float, WatchFormat.Double,
        }.Where(f => WatchValue.SupportsWidth(f, Address.ByteLength)).ToArray();

        FormatOptions = Formats.Select(DisplayOptions.For).ToArray();
        _selectedFormatOption = FormatOptions.First(o => o.Value == _format);

        Orders = Address.ByteLength > 1
            ? [ByteOrder.Dcba, ByteOrder.Abcd, ByteOrder.Badc, ByteOrder.Cdab]
            : [];

        OrderOptions = Orders.Select(DisplayOptions.For).ToArray();
        _selectedOrderOption = OrderOptions.FirstOrDefault(o => o.Value == _order);

        Show(_memory.ReadRaw(Address));
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
        DataSize.LWord => "LWORD",
        _ => "?",
    };

    /// <summary>이 주소 폭에서 쓸 수 있는 표시 형식.</summary>
    public IReadOnlyList<WatchFormat> Formats { get; }

    /// <summary>표시 형식 콤보 항목(한국어 이름 포함).</summary>
    public IReadOnlyList<DisplayOption<WatchFormat>> FormatOptions { get; }

    /// <summary>바이트 순서 콤보 항목(엔디안 설명 포함).</summary>
    public IReadOnlyList<DisplayOption<ByteOrder>> OrderOptions { get; }

    /// <summary>선택 가능한 바이트 순서. 1바이트 주소는 순서가 무의미하므로 비어 있다.</summary>
    public IReadOnlyList<ByteOrder> Orders { get; }

    /// <summary>바이트 순서 선택이 의미 있는지(2바이트 이상).</summary>
    public bool SupportsByteOrder => Address.ByteLength > 1;

    /// <summary>문서 생성 도구가 값 입력 후 형식을 일괄 적용할 때 쓰는 보관용 필드.</summary>
    public WatchFormat PendingFormat { get; set; } = WatchFormat.Decimal;

    /// <summary>이 행을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>현재 상태를 직렬화 형태로 만든다(프로젝트 저장용).</summary>
    public WatchEntry ToEntry() => Entry with { Format = Format, Order = Order };

    /// <summary>메모리가 외부에서 바뀐 경우에만 표시를 갱신한다.</summary>
    public void Refresh()
    {
        var raw = _memory.ReadRaw(Address);
        if (raw.AsSpan().SequenceEqual(_displayed))
        {
            return;
        }

        Show(raw);
        Error = null;
    }

    private void Show(byte[] memoryBytes)
    {
        _updating = true;
        try
        {
            _displayed = memoryBytes;
            ValueText = WatchValue.Render(memoryBytes, Format, Order);
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnFormatChanged(WatchFormat value)
    {
        if (_updating)
        {
            return;
        }

        var option = FormatOptions.FirstOrDefault(o => o.Value == value);
        if (option is not null && !ReferenceEquals(option, SelectedFormatOption))
        {
            SelectedFormatOption = option;
        }

        Show(_displayed);
    }

    partial void OnOrderChanged(ByteOrder value)
    {
        if (_updating)
        {
            return;
        }

        var option = OrderOptions.FirstOrDefault(o => o.Value == value);
        if (option is not null && !ReferenceEquals(option, SelectedOrderOption))
        {
            SelectedOrderOption = option;
        }

        Show(_displayed);
    }

    partial void OnSelectedFormatOptionChanged(DisplayOption<WatchFormat> value)
    {
        if (value is not null)
        {
            Format = value.Value;
        }
    }

    partial void OnSelectedOrderOptionChanged(DisplayOption<ByteOrder>? value)
    {
        if (value is not null)
        {
            Order = value.Value;
        }
    }

    partial void OnValueTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        var bytes = WatchValue.Parse(value, Address.ByteLength, Format, Order);
        if (bytes is null)
        {
            Error = Format is WatchFormat.Float or WatchFormat.Double
                ? "실수로 해석할 수 없습니다 (예: 3.14, -273.15)"
                : "값을 해석할 수 없습니다 (10진 / 0x16진 / 음수 / ON·OFF)";
            return;
        }

        Error = null;
        _displayed = bytes;
        _memory.WriteRaw(Address, bytes);
    }
}
