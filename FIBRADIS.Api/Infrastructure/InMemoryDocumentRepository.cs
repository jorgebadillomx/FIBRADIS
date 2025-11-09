using System.Collections.Concurrent;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<Guid, DocumentRecord> _documents = new();
    private readonly ConcurrentDictionary<string, Guid> _urlIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _hashIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, DocumentTextRecord> _texts = new();
    private readonly ConcurrentBag<DocumentJobEvent> _events = new();

    public Task<DocumentRecord?> GetByIdAsync(Guid documentId, CancellationToken ct)
    {
        if (_documents.TryGetValue(documentId, out var record))
        {
            return Task.FromResult<DocumentRecord?>(record);
        }

        return Task.FromResult<DocumentRecord?>(null);
    }

    public Task<DocumentRecord?> GetByUrlAsync(string url, CancellationToken ct)
    {
        if (_urlIndex.TryGetValue(url, out var id) && _documents.TryGetValue(id, out var record))
        {
            return Task.FromResult<DocumentRecord?>(record);
        }

        return Task.FromResult<DocumentRecord?>(null);
    }

    public Task<DocumentRecord?> GetByHashAsync(string hash, CancellationToken ct)
    {
        if (_hashIndex.TryGetValue(hash, out var id) && _documents.TryGetValue(id, out var record))
        {
            return Task.FromResult<DocumentRecord?>(record);
        }

        return Task.FromResult<DocumentRecord?>(null);
    }

    public Task<DocumentRecord> AddAsync(DocumentRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _documents[record.DocumentId] = record;
        _urlIndex[record.Url] = record.DocumentId;
        if (!string.IsNullOrWhiteSpace(record.Hash))
        {
            _hashIndex[record.Hash] = record.DocumentId;
        }

        return Task.FromResult(record);
    }

    public Task<DocumentRecord> UpdateAsync(DocumentRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _documents[record.DocumentId] = record;
        _urlIndex[record.Url] = record.DocumentId;
        if (!string.IsNullOrWhiteSpace(record.Hash))
        {
            _hashIndex[record.Hash] = record.DocumentId;
        }

        return Task.FromResult(record);
    }

    public Task SaveTextAsync(DocumentTextRecord text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        _texts[text.DocumentId] = text;
        return Task.CompletedTask;
    }

    public Task<DocumentTextRecord?> GetTextAsync(Guid documentId, CancellationToken ct)
    {
        if (_texts.TryGetValue(documentId, out var text))
        {
            return Task.FromResult<DocumentTextRecord?>(text);
        }

        return Task.FromResult<DocumentTextRecord?>(null);
    }

    public Task RecordJobEventAsync(DocumentJobEvent jobEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);
        _events.Add(jobEvent);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<DocumentJobEvent> Events => _events.ToArray();
}
