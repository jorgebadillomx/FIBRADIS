using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface ISummaryRepository
{
    Task<IReadOnlyList<DocumentSummaryCandidate>> GetPendingDocumentsAsync(string parserVersion, CancellationToken cancellationToken);
    Task SaveSummaryAsync(SummaryRecord summary, CancellationToken cancellationToken);
    Task MarkDocumentSummarizedAsync(Guid documentId, CancellationToken cancellationToken);
}
