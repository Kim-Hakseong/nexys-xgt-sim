using System.Globalization;
using Nxs.Core.Memory;

namespace Nxs.Core.Configuration;

/// <summary>한 모듈이 차지하는 메모리 범위 (PRD X-02 자동 매핑 결과).</summary>
public sealed record ModuleMapping
{
    /// <summary>베이스 번호.</summary>
    public required int BaseNumber { get; init; }

    /// <summary>슬롯 번호.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>장착 모듈.</summary>
    public required ModuleDefinition Module { get; init; }

    /// <summary>데이터가 놓이는 영역.</summary>
    public required MemoryArea Area { get; init; }

    /// <summary>영역 내 절대 시작 비트.</summary>
    public required int StartBit { get; init; }

    /// <summary>차지하는 비트 수.</summary>
    public required int BitLength { get; init; }

    /// <summary>영역 내 절대 시작 워드.</summary>
    public int StartWord => StartBit / 16;

    /// <summary>차지하는 워드 수.</summary>
    public int WordLength => BitLength / 16;

    /// <summary>시작 주소의 IEC 표기. 모듈의 자연 단위를 쓴다(디지털=X, 아날로그=W).</summary>
    public string StartAddressText => Module.PreferredView == DataSize.Word
        ? string.Create(CultureInfo.InvariantCulture, $"%{AreaLetter}W{StartWord}")
        : string.Create(CultureInfo.InvariantCulture, $"%{AreaLetter}X{StartBit}");

    private char AreaLetter => Area switch
    {
        MemoryArea.I => 'I',
        MemoryArea.Q => 'Q',
        MemoryArea.M => 'M',
        _ => throw new ArgumentOutOfRangeException(nameof(Area), Area, "알 수 없는 영역"),
    };

    /// <summary>디지털 점 하나의 주소를 구한다.</summary>
    /// <exception cref="ArgumentOutOfRangeException">점 번호가 모듈 점수를 벗어났을 때.</exception>
    /// <exception cref="InvalidOperationException">디지털 모듈이 아닐 때.</exception>
    public IecAddress PointAddress(int point)
    {
        if (Module.Kind is not (ModuleKind.DigitalInput or ModuleKind.DigitalOutput))
        {
            throw new InvalidOperationException($"{Module.ProductName}은 디지털 모듈이 아닙니다");
        }

        if (point < 0 || point >= Module.PointCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(point), point, $"점 번호는 0..{Module.PointCount - 1} 범위여야 합니다");
        }

        return IecAddress.Parse(
            string.Create(CultureInfo.InvariantCulture, $"%{AreaLetter}X{StartBit + point}"));
    }

    /// <summary>아날로그 채널 하나의 워드 주소를 구한다.</summary>
    /// <exception cref="ArgumentOutOfRangeException">채널 번호가 모듈 채널 수를 벗어났을 때.</exception>
    /// <exception cref="InvalidOperationException">아날로그 모듈이 아닐 때.</exception>
    public IecAddress ChannelAddress(int channel)
    {
        if (Module.Kind != ModuleKind.AnalogInput)
        {
            throw new InvalidOperationException($"{Module.ProductName}은 아날로그 모듈이 아닙니다");
        }

        if (channel < 0 || channel >= Module.ChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel, $"채널 번호는 0..{Module.ChannelCount - 1} 범위여야 합니다");
        }

        return IecAddress.Parse(
            string.Create(CultureInfo.InvariantCulture, $"%{AreaLetter}W{StartWord + channel}"));
    }
}
