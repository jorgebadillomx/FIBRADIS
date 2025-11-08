namespace FIBRADIS.Application.Models;

public sealed record SummaryRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FibraTicker { get; init; } = string.Empty;
    public string PeriodTag { get; init; } = string.Empty;
    public SummaryType Type { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokensUsed { get; init; }
    public decimal CostUsd { get; init; }
    public Guid SourceDocumentId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "system";
}
