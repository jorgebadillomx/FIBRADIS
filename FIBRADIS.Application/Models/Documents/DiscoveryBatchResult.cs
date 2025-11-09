namespace FIBRADIS.Application.Models.Documents;

public sealed record DiscoveryBatchResult
{
    public int Discovered { get; init; }
    public int SkippedByRobots { get; init; }
    public int Existing { get; init; }
    public IReadOnlyCollection<DocumentRecord> NewDocuments { get; init; } = Array.Empty<DocumentRecord>();
    public IReadOnlyDictionary<string, TimeSpan> AppliedDelays { get; init; } = new Dictionary<string, TimeSpan>();
}
