namespace Nxs.Core.Diagnostics;

/// <summary>트래픽 로그 방향 필터 — 3가지.</summary>
public enum TrafficDirectionFilter
{
    /// <summary>수신·송신 함께 (기본).</summary>
    RxAndTx,

    /// <summary>수신만 (마스터 → 시뮬레이터).</summary>
    RxOnly,

    /// <summary>송신만 (시뮬레이터 → 마스터).</summary>
    TxOnly,
}

/// <summary>방향 필터 표시 이름.</summary>
public static class TrafficDirectionFilterExtensions
{
    /// <summary>UI 표시 이름.</summary>
    public static string Label(this TrafficDirectionFilter filter) => filter switch
    {
        TrafficDirectionFilter.RxAndTx => "RX + TX 함께",
        TrafficDirectionFilter.RxOnly => "RX 만 (마스터 → 시뮬)",
        TrafficDirectionFilter.TxOnly => "TX 만 (시뮬 → 마스터)",
        _ => filter.ToString(),
    };

    /// <summary>이 방향 사건이 필터를 통과하는지.</summary>
    /// <remarks>
    /// 연결 수명 같은 Note 행은 방향이 없으므로 "함께" 에서만 보인다 —
    /// RX/TX 만 보려는 사용자에게 잡음이 되지 않게 한다.
    /// </remarks>
    public static bool Accepts(this TrafficDirectionFilter filter, TrafficDirection direction) => filter switch
    {
        TrafficDirectionFilter.RxAndTx => true,
        TrafficDirectionFilter.RxOnly => direction == TrafficDirection.Rx,
        TrafficDirectionFilter.TxOnly => direction == TrafficDirection.Tx,
        _ => true,
    };
}

/// <summary>
/// 트래픽 로그 조회 조건 — 방향 · 주소 · 오류 여부.
/// </summary>
/// <remarks>
/// 필터가 비어 있으면 통과시킨다(주소 목록이 비면 전 주소).
/// </remarks>
public sealed record TrafficFilter
{
    /// <summary>방향 필터.</summary>
    public TrafficDirectionFilter Direction { get; init; } = TrafficDirectionFilter.RxAndTx;

    /// <summary>대상 주소 표기. 비어 있으면 전 주소.</summary>
    public IReadOnlyCollection<string> Addresses { get; init; } = [];

    /// <summary>오류 행만 볼지.</summary>
    public bool ErrorsOnly { get; init; }

    /// <summary>기본 필터(전부 표시).</summary>
    public static TrafficFilter All { get; } = new();

    /// <summary>주소 필터가 걸려 있는지.</summary>
    public bool HasAddressFilter => Addresses.Count > 0;

    /// <summary>사건이 이 필터를 통과하는지.</summary>
    public bool Accepts(TrafficEvent trafficEvent)
    {
        ArgumentNullException.ThrowIfNull(trafficEvent);

        if (ErrorsOnly && !trafficEvent.IsError)
        {
            return false;
        }

        if (!Direction.Accepts(trafficEvent.Direction))
        {
            return false;
        }

        // 주소 필터가 걸려 있으면 주소를 모르는 행(연결 알림 등)은 숨긴다 —
        // 특정 주소만 보려는 의도에 맞다.
        if (HasAddressFilter && trafficEvent.Addresses.Count == 0)
        {
            return false;
        }

        return trafficEvent.TouchesAny(Addresses);
    }
}
