using System.Collections.Generic;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryDocumentDiscoveryService : IDocumentDiscoveryService
{
    private readonly List<DiscoveredDocument> _documents = new();
    private readonly object _sync = new();

    public Task<IReadOnlyCollection<DiscoveredDocument>> DiscoverAsync(DateTimeOffset since, IReadOnlyCollection<string>? domains, CancellationToken ct)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyCollection<DiscoveredDocument>>(_documents.ToArray());
        }
    }

    public void Add(DiscoveredDocument document)
    {
        lock (_sync)
        {
            _documents.Add(document);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _documents.Clear();
        }
    }
}
