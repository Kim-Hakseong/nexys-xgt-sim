using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// 디지털 점 하나. 입력 점은 사용자가 토글하고(→ 메모리 쓰기), 출력 점은 메모리를 반영만 한다(LED).
/// </summary>
public sealed partial class DigitalPointViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly bool _writable;
    private bool _suppressWrite;

    [ObservableProperty]
    private bool _isOn;

    /// <summary>점 뷰모델을 만든다.</summary>
    /// <param name="memory">공유 PLC 메모리.</param>
    /// <param name="address">이 점의 절대 비트 주소.</param>
    /// <param name="pointNumber">모듈 내 점 번호.</param>
    /// <param name="writable">참이면 사용자 토글이 메모리에 반영된다(입력 모듈).</param>
    public DigitalPointViewModel(PlcMemory memory, IecAddress address, int pointNumber, bool writable)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(address);

        _memory = memory;
        _writable = writable;
        Address = address;
        PointNumber = pointNumber;
        _isOn = memory.ReadBit(address);
    }

    /// <summary>절대 비트 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>모듈 내 점 번호.</summary>
    public int PointNumber { get; }

    /// <summary>점 번호 표시(2자리).</summary>
    public string PointLabel => PointNumber.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>주소 표기(툴팁·라벨용).</summary>
    public string AddressText => Address.Text;

    /// <summary>사용자 조작 가능 여부.</summary>
    public bool IsWritable => _writable;

    /// <summary>메모리 값을 뷰모델로 끌어온다. 이때는 되쓰기를 하지 않는다.</summary>
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
        if (_writable && !_suppressWrite)
        {
            _memory.WriteBit(Address, value);
        }
    }
}
