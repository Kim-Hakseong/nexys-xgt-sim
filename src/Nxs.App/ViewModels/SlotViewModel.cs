using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nxs.Core.Configuration;
using Nxs.Core.Memory;

namespace Nxs.App.ViewModels;

/// <summary>랙 슬롯 하나 (PRD X-05 — 슬롯별 패널).</summary>
public sealed partial class SlotViewModel : ObservableObject
{
    /// <summary>매핑된 슬롯의 뷰모델을 만든다.</summary>
    public SlotViewModel(PlcMemory memory, ModuleMapping mapping, NxpProject project)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(project);

        Mapping = mapping;
        SlotNumber = mapping.SlotNumber;
        Module = mapping.Module;

        switch (mapping.Module.Kind)
        {
            case ModuleKind.DigitalInput:
            case ModuleKind.DigitalOutput:
                var writable = mapping.Module.Kind == ModuleKind.DigitalInput;
                for (var p = 0; p < mapping.Module.PointCount; p++)
                {
                    Points.Add(new DigitalPointViewModel(memory, mapping.PointAddress(p), p, writable));
                }

                break;

            case ModuleKind.AnalogInput:
                for (var c = 0; c < mapping.Module.ChannelCount; c++)
                {
                    Channels.Add(new AnalogChannelViewModel(
                        memory,
                        mapping.ChannelAddress(c),
                        c,
                        project.ScaleFor(mapping.BaseNumber, mapping.SlotNumber, c)));
                }

                break;

            case ModuleKind.Communication:
            default:
                break;
        }
    }

    /// <summary>통신 모듈·빈 슬롯처럼 매핑이 없는 슬롯의 뷰모델을 만든다.</summary>
    public SlotViewModel(int slotNumber, ModuleDefinition? module)
    {
        SlotNumber = slotNumber;
        Module = module;
    }

    /// <summary>메모리 매핑. 매핑 없는 슬롯은 null.</summary>
    public ModuleMapping? Mapping { get; }

    /// <summary>슬롯 번호.</summary>
    public int SlotNumber { get; }

    /// <summary>장착 모듈. 빈 슬롯은 null.</summary>
    public ModuleDefinition? Module { get; }

    /// <summary>디지털 점 목록.</summary>
    public ObservableCollection<DigitalPointViewModel> Points { get; } = [];

    /// <summary>아날로그 채널 목록.</summary>
    public ObservableCollection<AnalogChannelViewModel> Channels { get; } = [];

    /// <summary>슬롯 제목. 예: <c>슬롯 2 · XGI-D24A</c>.</summary>
    public string Title => Module is null
        ? $"슬롯 {SlotNumber} · (빈 슬롯)"
        : $"슬롯 {SlotNumber} · {Module.ProductName}";

    /// <summary>부제 — 모듈 설명 + 할당 주소 범위.</summary>
    public string Subtitle
    {
        get
        {
            if (Module is null)
            {
                return "모듈 없음";
            }

            if (Mapping is null)
            {
                return $"{Module.Description} · 프로세스 데이터 없음";
            }

            var last = Module.PreferredView == DataSize.Word
                ? $"{Mapping.StartWord + Mapping.WordLength - 1}"
                : $"{Mapping.StartBit + Mapping.BitLength - 1}";
            return $"{Module.Description} · {Mapping.StartAddressText} ~ {last}";
        }
    }

    /// <summary>디지털 점 패널을 보일지.</summary>
    public bool HasPoints => Points.Count > 0;

    /// <summary>아날로그 채널 패널을 보일지.</summary>
    public bool HasChannels => Channels.Count > 0;

    /// <summary>사용자가 조작하는 입력 슬롯인지.</summary>
    public bool IsInputSlot => Module?.Kind == ModuleKind.DigitalInput;

    /// <summary>마스터가 쓰는 출력 슬롯인지(LED 표시).</summary>
    public bool IsOutputSlot => Module?.Kind == ModuleKind.DigitalOutput;

    /// <summary>메모리 값을 표시에 반영한다.</summary>
    public void Refresh()
    {
        foreach (var point in Points)
        {
            point.Refresh();
        }

        foreach (var channel in Channels)
        {
            channel.Refresh();
        }
    }
}
