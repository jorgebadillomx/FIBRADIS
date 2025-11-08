namespace FIBRADIS.Application.Exceptions;

public sealed class LlmRequestTimeoutException : Exception
{
    public LlmRequestTimeoutException(string message) : base(message)
    {
    }
}
