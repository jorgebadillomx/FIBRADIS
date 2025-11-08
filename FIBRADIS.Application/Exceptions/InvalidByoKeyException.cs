namespace FIBRADIS.Application.Exceptions;

public sealed class InvalidByoKeyException : Exception
{
    public InvalidByoKeyException(string message) : base(message)
    {
    }
}
