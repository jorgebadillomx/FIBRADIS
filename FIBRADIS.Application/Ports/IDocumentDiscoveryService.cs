using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IDocumentDiscoveryService
{
    Task<IReadOnlyCollection<DiscoveredDocument>> DiscoverAsync(DateTimeOffset since, IReadOnlyCollection<string>? domains, CancellationToken ct);
}
