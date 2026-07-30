using System.Globalization;
using Nxs.Core.Diagnostics;

namespace Nxs.App.ViewModels;

/// <summary>트래픽 로그 한 줄의 표시 모델 (PRD X-07).</summary>
public sealed class TrafficRowViewModel
{
    /// <summary>표시 모델을 만든다.</summary>
    public TrafficRowViewModel(TrafficEvent trafficEvent)
    {
        ArgumentNullException.ThrowIfNull(trafficEvent);
        Source = trafficEvent;
    }

    /// <summary>원본 사건.</summary>
    public TrafficEvent Source { get; }

    /// <summary>로컬 시각 표기(밀리초까지).</summary>
    public string TimeText => Source.Timestamp.ToLocalTime()
        .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>방향 표기.</summary>
    public string DirectionText => Source.Direction switch
    {
        TrafficDirection.Rx => "RX",
        TrafficDirection.Tx => "TX",
        TrafficDirection.Note => "--",
        _ => "??",
    };

    /// <summary>연결 식별자.</summary>
    public string ClientText => Source.ClientId;

    /// <summary>해석 요약.</summary>
    public string SummaryText => Source.Summary;

    /// <summary>raw hex.</summary>
    public string HexText => Source.RawHex;

    /// <summary>오류 행인지 — ErrorBrush 표시용.</summary>
    public bool IsError => Source.IsError;

    /// <summary>거절 사유 표기. 정상이면 빈 문자열.</summary>
    public string ReasonText => Source.IsError ? Source.Reason.ToString() : string.Empty;
}
