namespace FIBRADIS.Application.Exceptions;

public sealed class MissingFactsException : Exception
{
    public MissingFactsException(string message) : base(message)
    {
    }
}
