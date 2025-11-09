namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentProvenance
{
    public static readonly DocumentProvenance Empty = new();

    public string? Referer { get; init; }
    public string? CrawlPath { get; init; }
    public bool RobotsOk { get; init; }
    public string? ETag { get; init; }
    public IDictionary<string, string> AdditionalMetadata { get; init; } = new Dictionary<string, string>();
}
