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
    private readonly PlcMemory _memory;
    private readonly Action<AnalogPointViewModel>? _onRemove;
    private bool _updating;
    private byte[] _displayed = [];

    [ObservableProperty]
    private string _engineeringText = "0";

    [ObservableProperty]
    private string _rawText = "0";

    [ObservableProperty]
    private string? _error;

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
    public string OrderText => Entry.Order.Label();

    /// <summary>이 채널을 목록에서 제거한다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>현재 상태를 직렬화 형태로 만든다.</summary>
    public AnalogPointEntry ToEntry() => Entry;

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
            var raw = WatchValue.ToSigned(memoryBytes, Entry.Order);
            RawText = raw.ToString(CultureInfo.InvariantCulture);
            EngineeringText = Scale.ToEngineering((int)raw)
                .ToString("0.###", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    private void Commit(long raw, bool syncEngineering)
    {
        var (min, max) = WatchValue.SignedRange(Address.ByteLength);
        if (raw < min || raw > max)
        {
            Error = $"raw 값이 {SizeText} 범위({min} ~ {max})를 벗어났습니다";
            return;
        }

        Error = null;
        var bytes = WatchValue.FromSigned(raw, Address.ByteLength, Entry.Order);
        _displayed = bytes;
        _memory.WriteRaw(Address, bytes);

        _updating = true;
        try
        {
            if (syncEngineering)
            {
                EngineeringText = Scale.ToEngineering((int)raw)
                    .ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                RawText = raw.ToString(CultureInfo.InvariantCulture);
            }
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
            Error = "숫자가 아닙니다 (예: 5, 12.75, -3.2)";
            return;
        }

        try
        {
            Commit(Scale.ToRaw(eu), syncEngineering: false);
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

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            Error = "정수가 아닙니다";
            return;
        }

        try
        {
            Commit(raw, syncEngineering: true);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
    }
}
