namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentRecord
{
    public Guid DocumentId { get; init; }
    public string? Ticker { get; init; }
    public string Url { get; init; } = string.Empty;
    public string SourceDomain { get; init; } = string.Empty;
    public string? Hash { get; init; }
    public string ParserVersion { get; init; } = "1.0";
    public DateTimeOffset DiscoveredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? DownloadedAt { get; init; }
    public DateTimeOffset? ParsedAt { get; init; }
    public string? ContentType { get; init; }
    public DocumentKind Kind { get; init; }
    public DocumentStatus Status { get; init; }
    public decimal Confidence { get; init; }
    public DocumentProvenance Provenance { get; init; } = DocumentProvenance.Empty;
    public bool OcrUsed { get; init; }
    public int? Pages { get; init; }
    public string? PeriodTag { get; init; }
    public long? ContentLength { get; init; }
    public string? ETag { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public bool HasHash => !string.IsNullOrWhiteSpace(Hash);
}
