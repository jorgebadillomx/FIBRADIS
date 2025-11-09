using System.Collections.Concurrent;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryDocumentStorage : IDocumentStorage
{
    private readonly ConcurrentDictionary<Guid, DocumentContent> _documents = new();

    public Task<DocumentContent> GetDocumentAsync(Guid documentId, CancellationToken ct)
    {
        if (_documents.TryGetValue(documentId, out var document))
        {
            return Task.FromResult(Clone(document));
        }

        throw new KeyNotFoundException($"Document {documentId} was not found in storage.");
    }

    public Task SaveDocumentAsync(DocumentContent document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents[document.DocumentId] = Clone(document);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid documentId, string hash, CancellationToken ct)
    {
        if (_documents.TryGetValue(documentId, out var document))
        {
            return Task.FromResult(string.Equals(document.Hash, hash, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(false);
    }

    public void AddOrUpdate(DocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _documents[content.DocumentId] = Clone(content);
    }

    public bool TryGet(Guid documentId, out DocumentContent content)
    {
        if (_documents.TryGetValue(documentId, out var existing))
        {
            content = Clone(existing);
            return true;
        }

        content = null!;
        return false;
    }

    private static DocumentContent Clone(DocumentContent content)
    {
        return new DocumentContent(
            content.DocumentId,
            content.Hash,
            content.PublishedAt,
            content.DocumentDate,
            (byte[])content.Content.Clone(),
            content.IsImageBased);
    }
}
