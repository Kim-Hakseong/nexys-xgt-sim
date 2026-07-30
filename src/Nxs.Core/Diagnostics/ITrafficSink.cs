namespace Nxs.Core.Diagnostics;

/// <summary>트래픽 사건 수신처.</summary>
/// <remarks>
/// 서버는 여러 클라이언트 처리 스레드에서 동시에 호출한다 — 구현체는 **스레드 안전해야 한다**.
/// 또한 호출은 수신 루프 안에서 일어나므로 구현체는 블로킹하지 않아야 한다(UI 스레드 블로킹 금지, CLAUDE.md §3).
/// </remarks>
public interface ITrafficSink
{
    /// <summary>사건 한 건을 기록한다.</summary>
    void Record(TrafficEvent trafficEvent);
}
