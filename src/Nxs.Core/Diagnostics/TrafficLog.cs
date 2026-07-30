using System.Globalization;
using System.Text;

namespace Nxs.Core.Diagnostics;

/// <summary>
/// 트래픽 로그 (PRD X-07) — RX/TX raw hex + 해석 요약 + 타임스탬프, 오류 필터, 파일 저장.
/// </summary>
/// <remarks>
/// <para>
/// 고정 용량 링 버퍼다. 장시간 켜 두는 도구이므로 무한히 쌓이면 메모리를 잠식한다 —
/// 넘치면 가장 오래된 항목을 버리고 <see cref="DroppedCount"/>로 몇 건을 버렸는지 알린다
/// (조용히 사라지지 않게 한다).
/// </para>
/// <para>스레드 안전하며 <see cref="Record"/>는 락 구간이 짧아 수신 루프를 막지 않는다.</para>
/// </remarks>
public sealed class TrafficLog : ITrafficSink
{
    private readonly object _gate = new();
    private readonly Queue<TrafficEvent> _events;
    private readonly int _capacity;
    private int _errorCount;
    private int _droppedCount;

    /// <summary>로그를 만든다.</summary>
    /// <param name="capacity">보관 최대 건수.</param>
    /// <exception cref="ArgumentOutOfRangeException">용량이 1 미만일 때.</exception>
    public TrafficLog(int capacity = 5000)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "용량은 1 이상이어야 합니다");
        }

        _capacity = capacity;
        _events = new Queue<TrafficEvent>(Math.Min(capacity, 1024));
    }

    /// <summary>보관 중인 건수.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>보관 중인 오류 건수.</summary>
    public int ErrorCount
    {
        get
        {
            lock (_gate)
            {
                return _errorCount;
            }
        }
    }

    /// <summary>용량 초과로 버린 건수.</summary>
    public int DroppedCount
    {
        get
        {
            lock (_gate)
            {
                return _droppedCount;
            }
        }
    }

    /// <inheritdoc />
    public void Record(TrafficEvent trafficEvent)
    {
        ArgumentNullException.ThrowIfNull(trafficEvent);

        lock (_gate)
        {
            if (_events.Count >= _capacity)
            {
                var dropped = _events.Dequeue();
                if (dropped.IsError)
                {
                    _errorCount--;
                }

                _droppedCount++;
            }

            _events.Enqueue(trafficEvent);
            if (trafficEvent.IsError)
            {
                _errorCount++;
            }
        }
    }

    /// <summary>현재 내용의 독립 스냅샷을 만든다.</summary>
    /// <param name="errorsOnly">참이면 오류 행만.</param>
    public IReadOnlyList<TrafficEvent> Snapshot(bool errorsOnly = false)
    {
        lock (_gate)
        {
            return errorsOnly
                ? _events.Where(e => e.IsError).ToArray()
                : _events.ToArray();
        }
    }

    /// <summary>로그를 비운다.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _errorCount = 0;
            _droppedCount = 0;
        }
    }

    /// <summary>로그를 텍스트 파일로 저장한다.</summary>
    /// <param name="path">저장 경로.</param>
    /// <param name="errorsOnly">참이면 오류 행만 저장.</param>
    public void Save(string path, bool errorsOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Format(errorsOnly), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>로그를 텍스트로 만든다.</summary>
    /// <param name="errorsOnly">참이면 오류 행만.</param>
    public string Format(bool errorsOnly = false)
    {
        var events = Snapshot(errorsOnly);
        var dropped = DroppedCount;

        var sb = new StringBuilder();
        sb.AppendLine("# Nexys XGT Simulator — 트래픽 로그");
        sb.Append("# 저장 건수: ").Append(events.Count.ToString(CultureInfo.InvariantCulture));
        if (errorsOnly)
        {
            sb.Append(" (오류만)");
        }

        sb.AppendLine();
        if (dropped > 0)
        {
            sb.Append("# 용량 초과로 버려진 건수: ")
              .AppendLine(dropped.ToString(CultureInfo.InvariantCulture));
        }

        sb.AppendLine("# 시각(UTC)\t방향\t연결\t사유\t요약\traw hex");

        foreach (var e in events)
        {
            sb.Append(e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append('\t')
              .Append(DirectionLabel(e.Direction))
              .Append('\t')
              .Append(e.ClientId)
              .Append('\t')
              .Append(e.IsError ? e.Reason.ToString() : "-")
              .Append('\t')
              .Append(e.Summary)
              .Append('\t')
              .AppendLine(e.RawHex);
        }

        return sb.ToString();
    }

    private static string DirectionLabel(TrafficDirection direction) => direction switch
    {
        TrafficDirection.Rx => "RX",
        TrafficDirection.Tx => "TX",
        TrafficDirection.Note => "--",
        _ => "??",
    };
}
