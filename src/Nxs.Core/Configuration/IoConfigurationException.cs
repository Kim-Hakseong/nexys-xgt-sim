namespace Nxs.Core.Configuration;

/// <summary>I/O 구성이 주소 산법과 모순될 때 발생한다.</summary>
public sealed class IoConfigurationException : Exception
{
    /// <summary>구성 모순 예외를 만든다.</summary>
    public IoConfigurationException(string message) : base(message)
    {
    }
}
