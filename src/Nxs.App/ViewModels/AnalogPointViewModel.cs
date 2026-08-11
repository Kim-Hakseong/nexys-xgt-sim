using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 사용자 지정 A/D 채널 한 줄 — 공학단위와 raw 를 서로 변환해 보여주고 쓴다.
/// </summary>
/// <remarks>
/// 한쪽에 값을 넣으면 반대쪽이 채널 스케일에 따라 자동 변환된다.
/// 주기 갱신은 **외부 변경만** 반영해 입력 중인 텍스트를 되돌리지 않는다.
/// </remarks>
public sealed partial class AnalogPointViewModel : ObservableObject
{
    /// <summary>사용자 입력 직후 주기 갱신을 보류하는 시간(캐럿 보호).</summary>
    private static readonly TimeSpan EditGrace = TimeSpan.FromMilliseconds(1500);

    private readonly PlcMemory _memory;
    private readonly Action<AnalogPointViewModel>? _onRemove;
    private bool _updating;
    private byte[] _displayed = [];
    private DateTime _lastUserEditUtc = DateTime.MinValue;

    [ObservableProperty]
    private string _engineeringText = "0";

    [ObservableProperty]
    private string _rawText = "0";

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
    public AnalogPointViewModel(
        PlcMemory memory,
        AnalogPointEntry entry,
        AddressingOptions? addressing = null,
        Action<AnalogPointViewModel>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(entry);

        _memory = memory;
        _onRemove = onRemove;
        Entry = entry;
        Address = entry.Resolve(addressing);
        _format = entry.Format;
        _order = entry.Order;

        // ON/OFF 는 아날로그 채널에 의미가 없어 빼고, 폭에 맞는 형식만 노출한다.
        Formats = new[]
        {
            WatchFormat.Signed, WatchFormat.Decimal, WatchFormat.Hex,
            WatchFormat.Binary, WatchFormat.Float, WatchFormat.Double,
        }.Where(f => WatchValue.SupportsWidth(f, Address.ByteLength)).ToArray();

        FormatOptions = Formats.Select(DisplayOptions.For).ToArray();
        _selectedFormatOption = FormatOptions.FirstOrDefault(o => o.Value == _format)
            ?? FormatOptions[0];
        _format = _selectedFormatOption.Value;

        Orders = Address.ByteLength > 1
            ? [ByteOrder.Dcba, ByteOrder.Abcd, ByteOrder.Badc, ByteOrder.Cdab]
            : [];

        OrderOptions = Orders.Select(DisplayOptions.For).ToArray();
        _selectedOrderOption = OrderOptions.FirstOrDefault(o => o.Value == _order);

        Show(_memory.ReadRaw(Address));
    }

    /// <summary>원본 항목.</summary>
    public AnalogPointEntry Entry { get; }

    /// <summary>해석된 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>스케일.</summary>
    public AnalogChannelScale Scale => Entry.Scale;

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>사용자 별칭.</summary>
    public string Label => Entry.Label;

    /// <summary>공학단위 표기.</summary>
    public string UnitText => Scale.Unit;

    /// <summary>크기 배지.</summary>
    public string SizeText => Address.Size switch
    {
        DataSize.Byte => "BYTE",
        DataSize.Word => "WORD",
        DataSize.DWord => "DWORD",
        DataSize.LWord => "LWORD",
        _ => "?",
    };

    /// <summary>스케일 설명 — 공학단위 범위 ↔ raw 범위.</summary>
    public string ScaleText
        => $"{Scale.EngineeringMin:0.##} ~ {Scale.EngineeringMax:0.##} {Scale.Unit}"
            + $"  ↔  raw {Scale.RawMin} ~ {Scale.RawMax}";

    /// <summary>바이트 순서 표기.</summary>
    public string OrderText => Order.Label();

    /// <summary>이 주소 폭에서 쓸 수 있는 raw 표시 형식.</summary>
    public IReadOnlyList<WatchFormat> Formats { get; }

    /// <summary>raw 표시 형식 콤보 항목.</summary>
    public IReadOnlyList<DisplayOption<WatchFormat>> FormatOptions { get; }

    /// <summary>선택 가능한 바이트 순서. 1바이트 주소는 순서가 무의미하므로 비어 있다.</summary>
    public IReadOnlyList<ByteOrder> Orders { get; }

    /// <summary>바이트 순서 콤보 항목.</summary>
    public IReadOnlyList<DisplayOption<ByteOrder>> OrderOptions { get; }

    /// <summary>바이트 순서 선택이 의미 있는지(2바이트 이상).</summary>
    public bool SupportsByteOrder => Address.ByteLength > 1;

    /// <summary>이 채널을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>현재 상태를 직렬화 형태로 만든다.</summary>
    public AnalogPointEntry ToEntry() => Entry with { Format = Format, Order = Order };

    /// <summary>메모리가 외부에서 바뀐 경우에만 표시를 갱신한다.</summary>
    public void Refresh()
    {
        // 사용자가 방금 입력했다면 표시를 건드리지 않는다 (타이머가 캐럿을 뺏지 않도록).
        if (DateTime.UtcNow - _lastUserEditUtc < EditGrace)
        {
            return;
        }

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
            RawText = WatchValue.Render(memoryBytes, Format, Order);
            var raw = WatchValue.ToNumber(memoryBytes, Format, Order);
            EngineeringText = raw is null
                ? string.Empty
                : Scale.ToEngineering(raw.Value).ToString("0.###", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    private void Commit(double raw, bool syncEngineering)
    {
        var bytes = WatchValue.FromNumber(raw, Address.ByteLength, Format, Order);
        if (bytes is null)
        {
            Error = $"raw 값 {raw.ToString("0.###", CultureInfo.InvariantCulture)} 을 "
                + $"{SizeText} · {SelectedFormatOption.Label} 로 담을 수 없습니다";
            return;
        }

        Error = null;
        _displayed = bytes;
        _memory.WriteRaw(Address, bytes);

        _updating = true;
        try
        {
            if (syncEngineering)
            {
                EngineeringText = Scale.ToEngineering(raw)
                    .ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                RawText = WatchValue.Render(bytes, Format, Order);
            }
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

        // 형식만 바꾼다 — 메모리는 그대로 두고 같은 바이트를 다르게 읽는다.
        Error = null;
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

        Error = null;
        Show(_displayed);
        OnPropertyChanged(nameof(OrderText));
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

    /// <summary>
    /// 정수 형식이면 raw 를 반올림한다 — 공학단위 환산이 낸 소수부를 정수 형식에 담을 때
    /// 화면의 raw 와 메모리에 실제로 들어간 값이 어긋나지 않게 한다.
    /// </summary>
    private double RoundForFormat(double raw)
        => Format is WatchFormat.Float or WatchFormat.Double
            ? raw
            : Math.Round(raw, MidpointRounding.AwayFromZero);

    partial void OnEngineeringTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        _lastUserEditUtc = DateTime.UtcNow;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var eu))
        {
            Error = "숫자가 아닙니다 (예: 5, 12.75, -3.2)";
            return;
        }

        try
        {
            Commit(RoundForFormat(Scale.ToRawValue(eu)), syncEngineering: false);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
    }

    partial void OnRawTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        _lastUserEditUtc = DateTime.UtcNow;

        var bytes = WatchValue.Parse(value, Address.ByteLength, Format, Order);
        var raw = bytes is null ? null : WatchValue.ToNumber(bytes, Format, Order);
        if (raw is null)
        {
            Error = Format is WatchFormat.Float or WatchFormat.Double
                ? "실수로 해석할 수 없습니다 (예: 3.14, -273.15)"
                : "정수로 해석할 수 없습니다 (10진 / 0x16진)";
            return;
        }

        try
        {
            Commit(raw.Value, syncEngineering: true);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
    }
}
