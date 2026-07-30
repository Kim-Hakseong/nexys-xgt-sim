using Nxs.Core.Configuration;

namespace Nxs.App.ViewModels;

/// <summary>
/// 콤보박스 항목 — 값과 사람이 읽는 이름을 함께 들고 다닌다.
/// </summary>
/// <remarks>
/// enum 을 그대로 바인딩하면 <c>Dcba</c> 처럼 뜻이 전달되지 않는 이름이 보인다.
/// 값 변환기를 등록하는 대신 표시용 래퍼를 쓰는 쪽이 단순하다.
/// </remarks>
/// <typeparam name="T">감싸는 값 타입.</typeparam>
/// <param name="Value">실제 값.</param>
/// <param name="Label">표시 이름.</param>
public sealed record DisplayOption<T>(T Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>표시용 항목 목록.</summary>
public static class DisplayOptions
{
    /// <summary>바이트 순서 표시 이름.</summary>
    public static DisplayOption<ByteOrder> For(ByteOrder order) => new(order, order.Label());

    /// <summary>표시 형식의 한국어 이름.</summary>
    public static DisplayOption<WatchFormat> For(WatchFormat format) => new(format, format switch
    {
        WatchFormat.Decimal => "10진 (부호 없음)",
        WatchFormat.Signed => "10진 (부호 있음)",
        WatchFormat.Hex => "16진",
        WatchFormat.Binary => "2진",
        WatchFormat.Bool => "ON / OFF",
        WatchFormat.Float => "실수 Float (4B)",
        WatchFormat.Double => "실수 Double (8B)",
        _ => format.ToString(),
    });
}
