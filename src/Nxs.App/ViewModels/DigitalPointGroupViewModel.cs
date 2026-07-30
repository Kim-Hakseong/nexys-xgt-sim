using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 사용자 지정 디지털 점 그룹 — 주소 하나가 폭만큼의 비트로 펼쳐진다.
/// </summary>
/// <remarks>
/// <c>%MX</c>=1개 · <c>%MB</c>=8개 · <c>%MW</c>=16개 · <c>%MD</c>=32개 · <c>%ML</c>=64개.
/// 입력 모드는 각 비트를 토글할 수 있고, 출력 모드는 LED 로 표시만 한다.
/// 두 모드 모두 외부(마스터) 변경을 표시에 반영한다 — 양방향 확인.
/// </remarks>
public sealed partial class DigitalPointGroupViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly Action<DigitalPointGroupViewModel>? _onRemove;

    /// <summary>그룹을 만든다.</summary>
    public DigitalPointGroupViewModel(
        PlcMemory memory,
        DigitalPointEntry entry,
        AddressingOptions? addressing = null,
        Action<DigitalPointGroupViewModel>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(entry);

        _memory = memory;
        _onRemove = onRemove;
        Entry = entry;
        Address = entry.Resolve(addressing);
        BitCount = DigitalPointEntry.BitCountOf(Address);

        // 입력·출력 모두 사용자가 직접 켤 수 있다.
        // 출력을 감시 전용으로 두면 "마스터가 이 값을 읽었을 때 어떻게 되는지" 를 시험할 방법이 없다 —
        // 실장비에서는 PLC 프로그램이 %Q 를 쓰지만 시뮬레이터에서는 사람이 그 역할을 해야 한다.
        for (var i = 0; i < BitCount; i++)
        {
            Bits.Add(new DigitalPointViewModel(
                memory, DigitalPointEntry.BitAddressOf(Address, i), i, writable: true));
        }
    }

    /// <summary>원본 항목.</summary>
    public DigitalPointEntry Entry { get; }

    /// <summary>그룹 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>펼쳐진 비트 수.</summary>
    public int BitCount { get; }

    /// <summary>펼쳐진 비트들.</summary>
    public ObservableCollection<DigitalPointViewModel> Bits { get; } = [];

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>사용자 별칭.</summary>
    public string Label => Entry.Label;

    /// <summary>동작 방향.</summary>
    public DigitalPointMode Mode => Entry.Mode;

    /// <summary>
    /// 사용자가 토글할 수 있는지. **입력·출력 모두 참이다.**
    /// </summary>
    /// <remarks>
    /// 출력을 감시 전용으로 두면 마스터가 읽을 %Q 값을 만들 방법이 없다.
    /// 두 모드의 차이는 표시 방식(토글 버튼 vs LED)과 관용적 용도뿐이다.
    /// </remarks>
    public bool IsWritable => true;

    /// <summary>입력 모드인지(토글 버튼으로 표시).</summary>
    public bool IsInputMode => Entry.Mode == DigitalPointMode.Input;

    /// <summary>크기 배지.</summary>
    public string SizeText => Address.Size switch
    {
        DataSize.Bit => "BIT",
        DataSize.Byte => "BYTE",
        DataSize.Word => "WORD",
        DataSize.DWord => "DWORD",
        DataSize.LWord => "LWORD",
        _ => "?",
    };

    /// <summary>비트가 여러 개인지(배열로 보이는지).</summary>
    public bool IsArray => BitCount > 1;

    /// <summary>부제 — 비트 수와 펼쳐진 절대 주소 범위.</summary>
    public string Subtitle
    {
        get
        {
            if (!IsArray)
            {
                return IsInputMode ? "비트 1개 · 토글" : "비트 1개 · 마스터가 쓰는 값 (직접 조작도 가능)";
            }

            var first = Bits[0].AddressText;
            var last = Bits[^1].AddressText;
            return $"비트 {BitCount}개 · {first} ~ {last}";
        }
    }

    /// <summary>현재 값의 16진 표기(그룹 전체). 비트 주소는 ON/OFF.</summary>
    public string ValueText
    {
        get
        {
            if (!IsArray)
            {
                return Bits[0].IsOn ? "ON" : "OFF";
            }

            var bytes = _memory.ReadRaw(Address);
            return "0x" + Convert.ToHexString(bytes.Reverse().ToArray());
        }
    }

    /// <summary>이 그룹을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>모든 비트를 켠다(입력 모드에서만 의미 있다).</summary>
    [RelayCommand]
    private void SetAll() => SetEveryBit(true);

    /// <summary>모든 비트를 끈다.</summary>
    [RelayCommand]
    private void ClearAll() => SetEveryBit(false);

    private void SetEveryBit(bool value)
    {
        foreach (var bit in Bits)
        {
            bit.IsOn = value;
        }

        OnPropertyChanged(nameof(ValueText));
    }

    /// <summary>메모리 값을 표시에 반영한다(외부 변경 추적).</summary>
    public void Refresh()
    {
        foreach (var bit in Bits)
        {
            bit.Refresh();
        }

        OnPropertyChanged(nameof(ValueText));
    }

    /// <summary>펼쳐진 비트 번호 표기(UI 툴팁용).</summary>
    public static string BitLabel(int index)
        => index.ToString("D2", CultureInfo.InvariantCulture);
}
