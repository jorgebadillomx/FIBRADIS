namespace FIBRADIS.Application.Models.Documents;

public sealed record ParseResult
{
    public bool Success { get; init; }
    public bool RequiresRetry { get; init; }
    public string? FailureReason { get; init; }
    public DocumentTextRecord? TextRecord { get; init; }
    public DocumentRecord? Document { get; init; }
}
