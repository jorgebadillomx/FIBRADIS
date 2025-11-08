using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface ISummarizeService
{
    Task<IReadOnlyList<DocumentSummaryCandidate>> GetPendingDocumentsAsync(string parserVersion, CancellationToken cancellationToken);
    Task<SummarizeResult> SummarizeAsync(DocumentSummaryCandidate candidate, CancellationToken cancellationToken);
}
