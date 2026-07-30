using System.Buffers.Binary;
using System.Text;

namespace Nxs.Core.Protocol.Xgt;

/// <summary>
/// XGT FEnet 애플리케이션 헤더 (20바이트) — spec 초안 §1.
/// </summary>
/// <remarks>
/// 오프셋: 0..9 CompanyID · 10..11 PLCInfo · 12 CPUInfo · 13 Direction ·
/// 14..15 InvokeID · 16..17 Length · 18 Position · 19 BCC.
/// <para>
/// **신뢰도 '낮음' 필드(CPUInfo·Position)는 응답에서 요청 값을 에코한다.** 그렇게 하면 코덱이
/// 그 값의 정답을 몰라도 실장비처럼 보이는 응답을 만들 수 있다 — 맞혀야 하는 값의 수를 줄이는 설계다.
/// </para>
/// </remarks>
public readonly struct XgtFenetHeader
{
    /// <summary>헤더 길이.</summary>
    public const int Length = 20;

    /// <summary>Company ID ASCII (초안 §1, 신뢰도 높음).</summary>
    public const string CompanyId = "LSIS-XGT";

    /// <summary>요청 방향 바이트 (클라이언트 → PLC).</summary>
    public const byte DirectionRequest = 0x33;

    /// <summary>응답 방향 바이트 (PLC → 클라이언트).</summary>
    public const byte DirectionResponse = 0x11;

    /// <summary>PLC Info (예약).</summary>
    public ushort PlcInfo { get; init; }

    /// <summary>CPU Info. 값이 불확실하므로 에코 대상.</summary>
    public byte CpuInfo { get; init; }

    /// <summary>프레임 방향.</summary>
    public byte Direction { get; init; }

    /// <summary>Invoke ID. 응답에 그대로 에코한다.</summary>
    public ushort InvokeId { get; init; }

    /// <summary>헤더 뒤 데이터부 바이트 수.</summary>
    public ushort DataLength { get; init; }

    /// <summary>FEnet 모듈 슬롯 위치. 에코 대상.</summary>
    public byte Position { get; init; }

    /// <summary>수신된 BCC 바이트.</summary>
    public byte Bcc { get; init; }

    /// <summary>Company ID 가 기대값과 일치하는지.</summary>
    public bool HasExpectedCompanyId { get; init; }

    /// <summary>헤더 20바이트를 해석한다.</summary>
    /// <exception cref="ArgumentException">20바이트가 아닐 때.</exception>
    public static XgtFenetHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < Length)
        {
            throw new ArgumentException($"헤더는 {Length}바이트여야 합니다. 실제: {header.Length}", nameof(header));
        }

        return new XgtFenetHeader
        {
            HasExpectedCompanyId = MatchesCompanyId(header),
            PlcInfo = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]),
            CpuInfo = header[12],
            Direction = header[13],
            InvokeId = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
            DataLength = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]),
            Position = header[18],
            Bcc = header[19],
        };
    }

    /// <summary>Company ID 바이트가 <c>"LSIS-XGT"</c> 로 시작하는지 검사한다.</summary>
    public static bool MatchesCompanyId(ReadOnlySpan<byte> header)
    {
        if (header.Length < Length)
        {
            return false;
        }

        for (var i = 0; i < CompanyId.Length; i++)
        {
            if (header[i] != (byte)CompanyId[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 이 헤더를 응답 헤더로 바꿔 프레임 선두에 쓴다 (방향만 응답으로, 나머지는 에코).
    /// </summary>
    /// <param name="destination">최소 20바이트.</param>
    /// <param name="dataLength">데이터부 바이트 수.</param>
    public void WriteResponse(Span<byte> destination, ushort dataLength)
    {
        destination[..Length].Clear();
        Encoding.ASCII.GetBytes(CompanyId, destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], PlcInfo);
        destination[12] = CpuInfo;                 // 에코
        destination[13] = DirectionResponse;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..], InvokeId);  // 에코
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..], dataLength);
        destination[18] = Position;                // 에코
        destination[19] = ComputeBcc(destination[..Length]);
    }

    /// <summary>
    /// BCC = 헤더 0..18 바이트 합의 하위 바이트.
    /// </summary>
    /// <remarks>⚠️ 초안 §1 신뢰도 '낮음' — 계산 범위가 실장비와 다를 수 있다.</remarks>
    public static byte ComputeBcc(ReadOnlySpan<byte> header)
    {
        byte sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += header[i];
        }

        return sum;
    }
}
