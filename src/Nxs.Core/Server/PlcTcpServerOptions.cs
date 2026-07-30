using System.Net;

namespace Nxs.Core.Server;

/// <summary>TCP 서버 설정. 바인딩 IP(NIC 선택)·포트는 UI 설정 + .nxp 저장 대상 (DESIGN).</summary>
public sealed record PlcTcpServerOptions
{
    /// <summary>바인딩 IP. 기본은 모든 NIC.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>
    /// 접속 포트. **기본값이 없다** — 반드시 지정해야 한다.
    /// </summary>
    /// <remarks>
    /// [미정] spec/xgt-fenet-reference.md 에 XGT FEnet 기본 포트가 기재되지 않았다.
    /// 근거 없는 기본 포트를 넣으면 조작이 되므로(CLAUDE.md §3) 필수 설정으로 남긴다.
    /// 0을 주면 OS가 빈 포트를 배정한다(테스트용).
    /// </remarks>
    public required int Port { get; init; }

    /// <summary>수신 대기 큐 길이.</summary>
    public int Backlog { get; init; } = 16;

    /// <summary>동시 접속 허용 수. null이면 무제한.</summary>
    public int? MaxClients { get; init; }

    /// <summary>연결별 수신 버퍼 크기.</summary>
    public int ReceiveBufferSize { get; init; } = 4096;

    /// <summary>설정값을 검증한다.</summary>
    /// <exception cref="ArgumentException">포트/버퍼 크기가 유효 범위를 벗어났을 때.</exception>
    public void Validate()
    {
        if (Port is < 0 or > 65535)
        {
            throw new ArgumentException($"Port는 0..65535 범위여야 합니다. 실제: {Port}", nameof(Port));
        }

        if (ReceiveBufferSize < 1)
        {
            throw new ArgumentException(
                $"ReceiveBufferSize는 1 이상이어야 합니다. 실제: {ReceiveBufferSize}", nameof(ReceiveBufferSize));
        }

        if (MaxClients is < 1)
        {
            throw new ArgumentException(
                $"MaxClients는 1 이상이거나 null이어야 합니다. 실제: {MaxClients}", nameof(MaxClients));
        }
    }
}
