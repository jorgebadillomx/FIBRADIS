namespace FIBRADIS.Application.Models.Documents;

public sealed record DiscoveredDocument
{
    public string Url { get; init; } = string.Empty;
    public string? Ticker { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? KindHint { get; init; }
    public string? Title { get; init; }
    public string? Referer { get; init; }
    public string? CrawlPath { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
