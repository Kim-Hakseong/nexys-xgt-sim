using Nxs.Core.Protocol;

namespace Nxs.TestKit;

/// <summary>
/// 테스트 전용 길이 접두 프레이밍. **XGT 프로토콜이 아니다.**
/// </summary>
/// <remarks>
/// <see cref="StreamFrameAssembler"/>의 부분 수신 불변과 서버 파이프라인을 검증하기 위한 합성 규칙이다.
/// 실제 XGT 헤더 레이아웃은 spec/xgt-fenet-reference.md 기재 후에만 구현한다 (CLAUDE.md §3 조작 제로).
/// 헤더 4바이트: 0xAA 0x55 + payloadLength(uint16 LE). 전체 길이 = 4 + payloadLength.
/// </remarks>
public sealed class TestOnlyLengthPrefixFraming : IFrameLengthRule
{
    /// <summary>합성 매직 첫 바이트.</summary>
    public const byte Magic0 = 0xAA;

    /// <summary>합성 매직 둘째 바이트.</summary>
    public const byte Magic1 = 0x55;

    /// <summary>헤더 길이(4바이트).</summary>
    public int HeaderLength => 4;

    /// <inheritdoc />
    public bool TryGetTotalLength(ReadOnlySpan<byte> header, out int totalLength)
    {
        totalLength = 0;
        if (header[0] != Magic0 || header[1] != Magic1)
        {
            return false;
        }

        totalLength = HeaderLength + (header[2] | (header[3] << 8));
        return true;
    }

    /// <summary>페이로드를 프레임으로 감싼다.</summary>
    public static byte[] Wrap(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[0] = Magic0;
        frame[1] = Magic1;
        frame[2] = (byte)(payload.Length & 0xFF);
        frame[3] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>프레임에서 페이로드를 꺼낸다.</summary>
    public static ReadOnlySpan<byte> Unwrap(ReadOnlySpan<byte> frame) => frame[4..];

    /// <summary>테스트 프레임을 코드로 생성한다 (placeholder 데이터 금지 — CLAUDE.md §4).</summary>
    public static byte[] BuildFrame(int payloadLength, byte seed)
    {
        var payload = new byte[payloadLength];
        for (var i = 0; i < payloadLength; i++)
        {
            payload[i] = (byte)(seed + i);
        }

        return Wrap(payload);
    }
}
