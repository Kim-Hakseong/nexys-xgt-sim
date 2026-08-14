using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>묶음 한 줄 — 묶인 주소들과 그들이 공유하는 현재 값.</summary>
public sealed partial class LinkGroupViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly Action<LinkGroupViewModel>? _onRemove;
    private byte[] _displayed = [];

    [ObservableProperty]
    private string _valueText = string.Empty;

    /// <summary>줄을 만든다.</summary>
    public LinkGroupViewModel(
        PlcMemory memory, ResolvedLinkGroup group, Action<LinkGroupViewModel>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(group);

        _memory = memory;
        _onRemove = onRemove;
        Group = group;
        Refresh();
    }

    /// <summary>원본 묶음.</summary>
    public ResolvedLinkGroup Group { get; }

    /// <summary>묶인 주소 표기들.</summary>
    public IReadOnlyList<string> AddressTexts => Group.Members.Select(m => m.Text).ToArray();

    /// <summary>사용자 별칭.</summary>
    public string Label => Group.Label;

    /// <summary>별칭이 있는지.</summary>
    public bool HasLabel => Group.Label.Length > 0;

    /// <summary>크기 배지.</summary>
    public string SizeText => Group.Size switch
    {
        DataSize.Bit => "BIT",
        DataSize.Byte => "BYTE",
        DataSize.Word => "WORD",
        DataSize.DWord => "DWORD",
        DataSize.LWord => "LWORD",
        _ => "?",
    };

    /// <summary>이 묶음을 목록에서 뺀다.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>공유 값을 다시 읽는다. 바뀐 것이 없으면 알림을 내지 않는다.</summary>
    public void Refresh()
    {
        var raw = _memory.ReadRaw(Group.Members[0]);
        if (raw.AsSpan().SequenceEqual(_displayed))
        {
            return;
        }

        _displayed = raw;
        ValueText = Group.Size == DataSize.Bit
            ? (raw[0] != 0 ? "ON" : "OFF")
            : "0x" + Convert.ToHexString(WatchValue.ToMsbFirst(raw, ByteOrder.Dcba));
    }
}
