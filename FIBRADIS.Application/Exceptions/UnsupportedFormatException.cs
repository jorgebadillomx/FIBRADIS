namespace FIBRADIS.Application.Exceptions;

public sealed class UnsupportedFormatException : Exception
{
    public UnsupportedFormatException(string message)
        : base(message)
    {
    }
}
