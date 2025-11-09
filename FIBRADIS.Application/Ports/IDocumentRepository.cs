using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IDocumentRepository
{
    Task<DocumentRecord?> GetByIdAsync(Guid documentId, CancellationToken ct);
    Task<DocumentRecord?> GetByUrlAsync(string url, CancellationToken ct);
    Task<DocumentRecord?> GetByHashAsync(string hash, CancellationToken ct);
    Task<DocumentRecord> AddAsync(DocumentRecord record, CancellationToken ct);
    Task<DocumentRecord> UpdateAsync(DocumentRecord record, CancellationToken ct);
    Task SaveTextAsync(DocumentTextRecord text, CancellationToken ct);
    Task<DocumentTextRecord?> GetTextAsync(Guid documentId, CancellationToken ct);
    Task RecordJobEventAsync(DocumentJobEvent jobEvent, CancellationToken ct);
}
