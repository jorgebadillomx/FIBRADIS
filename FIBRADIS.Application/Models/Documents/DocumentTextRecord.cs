namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentTextRecord
{
    public Guid DocumentId { get; init; }
    public string? Text { get; init; }
    public string? TablesJson { get; init; }
    public bool OcrUsed { get; init; }
    public int? Pages { get; init; }
    public string ParserVersion { get; init; } = string.Empty;
    public DateTimeOffset ParsedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metrics { get; init; } = new Dictionary<string, string>();
}
