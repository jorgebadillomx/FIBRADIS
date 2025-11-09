using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IDocumentStorage
{
    Task<DocumentContent> GetDocumentAsync(Guid documentId, CancellationToken ct);
    Task SaveDocumentAsync(DocumentContent document, CancellationToken ct);
    Task<bool> ExistsAsync(Guid documentId, string hash, CancellationToken ct);
}
