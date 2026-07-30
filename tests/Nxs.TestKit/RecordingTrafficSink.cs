using Nxs.Core.Diagnostics;

namespace Nxs.TestKit;

/// <summary>테스트용 스레드 안전 트래픽 수집기.</summary>
public sealed class RecordingTrafficSink : ITrafficSink
{
    private readonly object _gate = new();
    private readonly List<TrafficEvent> _events = [];

    /// <inheritdoc />
    public void Record(TrafficEvent trafficEvent)
    {
        lock (_gate)
        {
            _events.Add(trafficEvent);
        }
    }

    /// <summary>수집된 사건의 스냅샷.</summary>
    public IReadOnlyList<TrafficEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    /// <summary>지정 방향의 사건만 반환한다.</summary>
    public IReadOnlyList<TrafficEvent> OfDirection(TrafficDirection direction)
        => Events.Where(e => e.Direction == direction).ToArray();
}
