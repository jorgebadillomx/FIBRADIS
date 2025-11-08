namespace FIBRADIS.Application.Models;

public sealed record NewsRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string UrlHash { get; init; } = string.Empty;
    public DateTimeOffset PublishedAt { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? FibraTicker { get; init; }
    public string? Sector { get; init; }
    public NewsSentiment Sentiment { get; init; } = NewsSentiment.Neutral;
    public NewsStatus Status { get; init; } = NewsStatus.Pending;
    public int TokensUsed { get; init; }
    public decimal CostUsd { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "system";
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
