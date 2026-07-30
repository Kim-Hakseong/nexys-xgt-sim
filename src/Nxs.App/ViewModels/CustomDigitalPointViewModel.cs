using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 사용자 지정 디지털 점 — 임의 비트 주소를 토글(입력)하거나 LED 로 감시(출력)한다.
/// </summary>
/// <remarks>
/// 랙 매핑은 고정 주소만 보여주므로 마스터가 실제로 쓰는 <c>%MX801</c> 같은 비트를 확인할 수 없었다.
/// 입력 모드는 사용자 토글이 메모리에 반영되고(마스터가 읽어 확인),
/// 출력 모드는 마스터가 쓴 값을 표시한다 — **양방향 검증**이 된다.
/// 두 모드 모두 외부 변경을 표시에 반영한다.
/// </remarks>
public sealed partial class CustomDigitalPointViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly Action<CustomDigitalPointViewModel>? _onRemove;
    private bool _suppressWrite;

    [ObservableProperty]
    private bool _isOn;

    /// <summary>점을 만든다.</summary>
    public CustomDigitalPointViewModel(
        PlcMemory memory,
        DigitalPointEntry entry,
        AddressingOptions? addressing = null,
        Action<CustomDigitalPointViewModel>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(entry);

        _memory = memory;
        _onRemove = onRemove;
        Entry = entry;
        Address = entry.Resolve(addressing);
        _isOn = memory.ReadBit(Address);
    }

    /// <summary>원본 항목.</summary>
    public DigitalPointEntry Entry { get; }

    /// <summary>해석된 비트 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>사용자 별칭.</summary>
    public string Label => Entry.Label;

    /// <summary>동작 방향.</summary>
    public DigitalPointMode Mode => Entry.Mode;

    /// <summary>사용자가 토글할 수 있는지 (입력 모드만).</summary>
    public bool IsWritable => Entry.Mode == DigitalPointMode.Input;

    /// <summary>현재 상태 문구.</summary>
    public string StateText => IsOn ? "ON" : "OFF";

    /// <summary>이 점을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>메모리 값을 표시에 반영한다(외부 변경 추적).</summary>
    public void Refresh()
    {
        var actual = _memory.ReadBit(Address);
        if (actual == IsOn)
        {
            return;
        }

        _suppressWrite = true;
        try
        {
            IsOn = actual;
        }
        finally
        {
            _suppressWrite = false;
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));

        if (IsWritable && !_suppressWrite)
        {
            _memory.WriteBit(Address, value);
        }
    }
}
