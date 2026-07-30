namespace Nxs.Core.Protocol;

/// <summary>
/// 클라이언트별 수신 상태머신 — "헤더 완독 → 길이만큼 완독" (DESIGN).
/// </summary>
/// <remarks>
/// <para>
/// 부분 수신 불변: TCP는 스트림이므로 프레임이 임의 위치에서 쪼개져 도착한다.
/// 이 클래스는 도착 청크 경계와 무관하게 동일한 프레임 시퀀스를 산출해야 한다
/// (CLAUDE.md §4.2 — 1바이트 주입 테스트 필수).
/// </para>
/// <para>
/// 프레임 경계 판정은 <see cref="IFrameLengthRule"/>로 주입된다 — 이 클래스에는
/// XGT 프레임 세부가 없다.
/// </para>
/// <para>스레드 안전하지 않다. 연결 하나당 인스턴스 하나로 사용한다.</para>
/// </remarks>
public sealed class StreamFrameAssembler
{
    private static readonly byte[][] None = [];

    private readonly IFrameLengthRule _rule;
    private readonly int _maxFrameLength;
    private byte[] _buffer;
    private int _count;

    /// <summary>상태머신을 만든다.</summary>
    /// <param name="rule">프레임 경계 판정 규칙.</param>
    /// <param name="maxFrameLength">허용 최대 프레임 길이(헤더 포함). 초과 선언은 프레이밍 위반.</param>
    public StreamFrameAssembler(IFrameLengthRule rule, int maxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.HeaderLength < 1)
        {
            throw new ArgumentException($"HeaderLength는 1 이상이어야 합니다. 실제: {rule.HeaderLength}", nameof(rule));
        }

        if (maxFrameLength < rule.HeaderLength)
        {
            throw new ArgumentException(
                $"maxFrameLength({maxFrameLength})는 HeaderLength({rule.HeaderLength}) 이상이어야 합니다",
                nameof(maxFrameLength));
        }

        _rule = rule;
        _maxFrameLength = maxFrameLength;
        _buffer = new byte[Math.Min(maxFrameLength, Math.Max(256, rule.HeaderLength))];
    }

    /// <summary>아직 프레임을 완성하지 못한 채 보류 중인 바이트 수.</summary>
    public int BufferedByteCount => _count;

    /// <summary>
    /// 수신 바이트를 밀어넣고 완성된 프레임들을 도착 순서대로 반환한다.
    /// 프레임을 완성하지 못하면 빈 목록을 반환하고 바이트를 보류한다.
    /// </summary>
    /// <exception cref="FramingException">헤더를 해석할 수 없거나 선언 길이가 허용 범위를 벗어났을 때.</exception>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> data)
    {
        if (!data.IsEmpty)
        {
            Append(data);
        }

        List<byte[]>? frames = null;
        var consumed = 0;

        while (true)
        {
            var available = _count - consumed;
            if (available < _rule.HeaderLength)
            {
                break;
            }

            var header = _buffer.AsSpan(consumed, _rule.HeaderLength);
            if (!_rule.TryGetTotalLength(header, out var totalLength))
            {
                throw new FramingException(
                    $"헤더를 해석할 수 없습니다: {Hex.Format(header)}");
            }

            if (totalLength < _rule.HeaderLength || totalLength > _maxFrameLength)
            {
                throw new FramingException(
                    $"선언된 프레임 길이 {totalLength}가 허용 범위[{_rule.HeaderLength}, {_maxFrameLength}]를 벗어났습니다");
            }

            if (available < totalLength)
            {
                break;
            }

            (frames ??= []).Add(_buffer.AsSpan(consumed, totalLength).ToArray());
            consumed += totalLength;
        }

        if (consumed > 0)
        {
            _buffer.AsSpan(consumed, _count - consumed).CopyTo(_buffer);
            _count -= consumed;
        }

        return frames ?? (IReadOnlyList<byte[]>)None;
    }

    /// <summary>보류 중인 바이트를 버린다. 프레이밍 위반 후 재동기화용.</summary>
    public void Reset() => _count = 0;

    private void Append(ReadOnlySpan<byte> data)
    {
        var needed = _count + data.Length;
        if (needed > _buffer.Length)
        {
            // 최대 프레임 길이를 넘는 보류는 있을 수 없다 — 그 전에 프레이밍 위반으로 걸러진다.
            // 다만 여러 프레임이 한 청크에 몰려오면 버퍼는 청크 크기까지 커질 수 있다.
            var capacity = Math.Max(needed, _buffer.Length * 2);
            Array.Resize(ref _buffer, capacity);
        }

        data.CopyTo(_buffer.AsSpan(_count));
        _count = needed;
    }
}
