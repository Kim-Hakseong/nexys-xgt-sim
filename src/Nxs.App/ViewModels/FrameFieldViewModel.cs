using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Protocol.Xgt;

namespace Nxs.App.ViewModels;

/// <summary>프레임 전문에서 이름 붙은 한 구간(헤더 필드·변수명·값 …).</summary>
public sealed partial class FrameFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>구간 뷰모델을 만든다.</summary>
    public FrameFieldViewModel(FrameField field, int index)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
        Index = index;
    }

    /// <summary>원본 구간.</summary>
    public FrameField Field { get; }

    /// <summary>구간 목록에서의 순번 — 바이트 칸이 자기 구간을 가리키는 데 쓴다.</summary>
    public int Index { get; }

    /// <summary>구간 이름.</summary>
    public string Name => Field.Name;

    /// <summary>해석한 값.</summary>
    public string Value => Field.Value;

    /// <summary>바이트 위치 표기.</summary>
    public string RangeText => Field.RangeText;

    /// <summary>이 구간이 가리키는 주소(없으면 null).</summary>
    public string? Address => Field.Address;

    /// <summary>구간 종류 — 색 구분용.</summary>
    public FrameFieldKind Kind => Field.Kind;

    /// <summary>헤더 구간인지.</summary>
    public bool IsHeader => Field.Kind == FrameFieldKind.Header;

    /// <summary>변수명 구간인지.</summary>
    public bool IsName => Field.Kind == FrameFieldKind.Name;

    /// <summary>값 구간인지.</summary>
    public bool IsValue => Field.Kind == FrameFieldKind.Value;

    /// <summary>해석하지 못한 구간인지.</summary>
    public bool IsUnknown => Field.Kind == FrameFieldKind.Unknown;
}

/// <summary>프레임 전문의 바이트 한 칸.</summary>
public sealed partial class FrameByteViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>바이트 칸을 만든다.</summary>
    /// <param name="offset">프레임 안에서의 위치.</param>
    /// <param name="value">바이트 값.</param>
    /// <param name="fieldIndex">이 바이트가 속한 구간의 순번(-1이면 어디에도 속하지 않음).</param>
    /// <param name="kind">속한 구간의 종류.</param>
    public FrameByteViewModel(int offset, byte value, int fieldIndex, FrameFieldKind kind)
    {
        Offset = offset;
        Value = value;
        FieldIndex = fieldIndex;
        Kind = kind;
    }

    /// <summary>프레임 안에서의 위치.</summary>
    public int Offset { get; }

    /// <summary>바이트 값.</summary>
    public byte Value { get; }

    /// <summary>속한 구간의 순번.</summary>
    public int FieldIndex { get; }

    /// <summary>속한 구간의 종류.</summary>
    public FrameFieldKind Kind { get; }

    /// <summary>16진 두 자리 표기.</summary>
    public string HexText => Value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>헤더 바이트인지 — 앞 20바이트를 배경색으로 구분한다.</summary>
    public bool IsHeader => Kind == FrameFieldKind.Header;

    /// <summary>변수명 바이트인지.</summary>
    public bool IsName => Kind == FrameFieldKind.Name;

    /// <summary>값 바이트인지.</summary>
    public bool IsValue => Kind == FrameFieldKind.Value;

    /// <summary>해석하지 못한 바이트인지.</summary>
    public bool IsUnknown => Kind == FrameFieldKind.Unknown;
}
