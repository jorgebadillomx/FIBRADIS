namespace FIBRADIS.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
