namespace Nxs.Core.Protocol;

/// <summary>
/// 바이트 스트림이 프레이밍 규칙을 위반했을 때 발생한다.
/// 서버는 이 예외를 받으면 해당 연결의 수신 상태를 신뢰할 수 없으므로 연결을 닫는다.
/// </summary>
public sealed class FramingException : Exception
{
    /// <summary>프레이밍 위반 예외를 만든다.</summary>
    public FramingException(string message) : base(message)
    {
    }
}
