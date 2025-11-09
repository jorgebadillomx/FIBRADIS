using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IFactsExtractor
{
    Task<FactsResult> ExtractAsync(DocumentFactsRequest request, CancellationToken ct);
}
