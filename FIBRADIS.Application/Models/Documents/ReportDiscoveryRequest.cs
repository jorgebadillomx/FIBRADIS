namespace FIBRADIS.Application.Models.Documents;

public sealed record ReportDiscoveryRequest
{
    public DateTimeOffset? Since { get; init; }
    public IReadOnlyCollection<string>? Domains { get; init; }
}
