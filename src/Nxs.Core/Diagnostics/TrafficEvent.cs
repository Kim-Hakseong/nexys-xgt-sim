using Nxs.Core.Protocol;

namespace Nxs.Core.Diagnostics;

/// <summary>트래픽 방향.</summary>
public enum TrafficDirection
{
    /// <summary>수신 (마스터 → 시뮬레이터).</summary>
    Rx,

    /// <summary>송신 (시뮬레이터 → 마스터).</summary>
    Tx,

    /// <summary>연결 수명 주기 등 프레임이 아닌 사건.</summary>
    Note,
}

/// <summary>트래픽 로그 한 줄 (PRD X-07 — RX/TX raw hex + 해석 요약 + 타임스탬프).</summary>
public sealed record TrafficEvent
{
    private static readonly byte[] NoBytes = [];

    /// <summary>발생 시각(UTC). <see cref="Time.ITimeSource"/>에서 받는다.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>방향.</summary>
    public required TrafficDirection Direction { get; init; }

    /// <summary>연결 식별자. 멀티클라이언트 구분용.</summary>
    public required string ClientId { get; init; }

    /// <summary>raw 바이트. 프레임이 아닌 사건은 빈 배열.</summary>
    public byte[] Raw { get; init; } = NoBytes;

    /// <summary>사람이 읽는 해석 요약.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>거절 사유. 정상이면 <see cref="PlcErrorReason.None"/>.</summary>
    public PlcErrorReason Reason { get; init; } = PlcErrorReason.None;

    /// <summary>이 사건이 건드린 주소 표기. 주소 필터가 사용한다.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>
    /// 지정 주소 중 하나라도 건드렸는지. 필터가 비어 있으면 항상 참.
    /// </summary>
    public bool TouchesAny(IReadOnlyCollection<string> addresses)
    {
        if (addresses.Count == 0)
        {
            return true;
        }

        foreach (var address in Addresses)
        {
            foreach (var wanted in addresses)
            {
                if (string.Equals(address, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>오류 행인지 (로그 필터·ErrorBrush 표시용).</summary>
    public bool IsError => Reason != PlcErrorReason.None;

    /// <summary>raw 바이트의 hex 표기.</summary>
    public string RawHex => Hex.Format(Raw);
}
