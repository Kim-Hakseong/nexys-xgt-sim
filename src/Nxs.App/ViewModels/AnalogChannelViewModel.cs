using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>
/// AD 채널 하나. 사용자가 공학단위 또는 raw 로 값을 넣으면 스케일 변환 후 메모리 워드에 쓴다 (PRD X-05).
/// </summary>
public sealed partial class AnalogChannelViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private bool _updating;

    /// <summary>
    /// 현재 표시 중인 raw 값. <see cref="Refresh"/>가 **외부 변경만** 반영하도록 하는 기준이다.
    /// 이것이 없으면 사용자가 "1." 까지 입력한 순간(파스 실패 → 메모리 미변경) 주기 갱신이
    /// 입력칸을 이전 값으로 되돌려 타이핑을 방해한다.
    /// </summary>
    private int _displayedRaw;

    [ObservableProperty]
    private string _engineeringText = "0";

    [ObservableProperty]
    private string _rawText = "0";

    [ObservableProperty]
    private string? _error;

    /// <summary>채널 뷰모델을 만든다.</summary>
    public AnalogChannelViewModel(PlcMemory memory, IecAddress address, int channel, AnalogChannelScale scale)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(scale);

        _memory = memory;
        Address = address;
        Channel = channel;
        Scale = scale;
        Show(AnalogChannelScale.WordToRaw((ushort)memory.ReadScalar(address)));
    }

    /// <summary>채널의 워드 주소.</summary>
    public IecAddress Address { get; }

    /// <summary>모듈 내 채널 번호.</summary>
    public int Channel { get; }

    /// <summary>이 채널의 스케일.</summary>
    public AnalogChannelScale Scale { get; }

    /// <summary>채널 라벨.</summary>
    public string ChannelLabel => $"CH{Channel.ToString("D2", CultureInfo.InvariantCulture)}";

    /// <summary>주소 표기.</summary>
    public string AddressText => Address.Text;

    /// <summary>공학단위 표기(비어 있으면 raw 통과).</summary>
    public string UnitText => Scale.Unit;

    /// <summary>
    /// 메모리 워드가 외부(마스터·자동화)에서 바뀐 경우에만 표시를 갱신한다.
    /// 사용자 입력 중에는 값이 같으므로 아무것도 하지 않는다.
    /// </summary>
    public void Refresh()
    {
        var raw = AnalogChannelScale.WordToRaw((ushort)_memory.ReadScalar(Address));
        if (raw == _displayedRaw)
        {
            // 메모리가 그대로면 외부 변경이 없었다는 뜻이다 — 입력 중인 텍스트를 건드리지 않는다.
            // (입력이 아직 유효하지 않아 Error 가 떠 있는 경우도 포함: 그 텍스트를 되돌리면 타이핑이 끊긴다.)
            return;
        }

        Show(raw);
        Error = null;
    }

    private void Show(int raw)
    {
        _updating = true;
        try
        {
            _displayedRaw = raw;
            RawText = raw.ToString(CultureInfo.InvariantCulture);
            EngineeringText = Scale.ToEngineering(raw).ToString("0.###", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnEngineeringTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var eu))
        {
            Error = "숫자가 아닙니다";
            return;
        }

        int raw;
        try
        {
            raw = Scale.ToRaw(eu);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return;
        }

        Error = null;
        _memory.WriteScalar(Address, AnalogChannelScale.RawToWord(raw));

        _updating = true;
        try
        {
            _displayedRaw = raw;
            RawText = raw.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnRawTextChanged(string value)
    {
        if (_updating)
        {
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            Error = "정수가 아닙니다";
            return;
        }

        if (raw is < short.MinValue or > ushort.MaxValue)
        {
            Error = "워드 범위를 벗어났습니다";
            return;
        }

        Error = null;
        _memory.WriteScalar(Address, AnalogChannelScale.RawToWord(raw));

        _updating = true;
        try
        {
            _displayedRaw = raw;
            EngineeringText = Scale.ToEngineering(raw).ToString("0.###", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }
}
