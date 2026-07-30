using System.Buffers.Binary;

namespace Nxs.Core.Protocol.Xgt;

/// <summary>
/// XGT FEnet 프레임 경계 규칙 — "헤더 20바이트 완독 → Length 필드만큼 더 완독".
/// </summary>
/// <remarks>
/// ⚠️ Length 필드가 "데이터부 길이"라는 초안 §1 가정에 의존한다(신뢰도 높음이지만 미검증).
/// 캡처 프레임 1개로 즉시 확정 가능: <c>전체 길이 - 20 == Length 필드값</c> 인지 확인하면 된다.
/// </remarks>
public sealed class XgtFenetFraming : IFrameLengthRule
{
    private readonly bool _validateCompanyId;

    /// <summary>규칙을 만든다.</summary>
    /// <param name="validateCompanyId">
    /// Company ID 를 검사할지. 검사하면 쓰레기 바이트를 조기에 잡지만, 실장비의 Company ID 가
    /// 기대와 다르면 연결을 닫는다(트래픽 로그에 raw hex 가 남아 진단 가능).
    /// </param>
    public XgtFenetFraming(bool validateCompanyId = true) => _validateCompanyId = validateCompanyId;

    /// <inheritdoc />
    public int HeaderLength => XgtFenetHeader.Length;

    /// <inheritdoc />
    public bool TryGetTotalLength(ReadOnlySpan<byte> header, out int totalLength)
    {
        totalLength = 0;

        if (_validateCompanyId && !XgtFenetHeader.MatchesCompanyId(header))
        {
            return false;
        }

        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
        totalLength = XgtFenetHeader.Length + dataLength;
        return true;
    }
}
