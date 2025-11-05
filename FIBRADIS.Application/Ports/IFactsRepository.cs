using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IFactsRepository
{
    Task<DocumentFactsRecord?> GetDocumentFactsAsync(Guid documentId, string parserVersion, string hash, CancellationToken ct);
    Task SaveDocumentFactsAsync(DocumentFactsRecord record, CancellationToken ct);
    Task SavePendingReviewAsync(DocumentFactsRecord record, CancellationToken ct);
    Task AppendHistoryAsync(FactsHistoryRecord record, CancellationToken ct);
    Task MarkSupersededAsync(string fibraTicker, string periodTag, CancellationToken ct);
}
