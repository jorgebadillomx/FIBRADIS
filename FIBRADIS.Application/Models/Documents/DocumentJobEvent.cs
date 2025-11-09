namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentJobEvent
{
    public Guid JobRunId { get; init; }
    public Guid DocumentId { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration => CompletedAt - StartedAt;
    public string? Hash { get; init; }
    public string? ParserVersion { get; init; }
    public string? Details { get; init; }
    public bool Success { get; init; }
}
