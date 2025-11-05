using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IDocumentStorage
{
    Task<DocumentContent> GetDocumentAsync(Guid documentId, CancellationToken ct);
}
