namespace FIBRADIS.Application.Exceptions;

public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message)
    {
    }
}
