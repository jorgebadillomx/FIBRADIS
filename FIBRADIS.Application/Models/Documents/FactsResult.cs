namespace FIBRADIS.Application.Models.Documents;

public sealed record FactsResult
{
    public bool Success { get; init; }
    public bool RequiresReview { get; init; }
    public int Score { get; init; }
    public DocumentFactsRecord? Facts { get; init; }
    public DocumentRecord? Document { get; init; }
    public string? FailureReason { get; init; }
}
