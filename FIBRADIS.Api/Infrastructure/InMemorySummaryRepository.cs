using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySummaryRepository : ISummaryRepository
{
    private readonly ConcurrentDictionary<Guid, (DocumentSummaryCandidate Candidate, bool Summarized)> _documents = new();
    private readonly ConcurrentDictionary<Guid, SummaryRecord> _summaries = new();

    public Task<IReadOnlyList<DocumentSummaryCandidate>> GetPendingDocumentsAsync(string parserVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = _documents.Values
            .Where(tuple => !tuple.Summarized && string.Equals(tuple.Candidate.ParserVersion, parserVersion, StringComparison.OrdinalIgnoreCase))
            .Select(tuple => tuple.Candidate)
            .ToArray();
        return Task.FromResult<IReadOnlyList<DocumentSummaryCandidate>>(items);
    }

    public Task SaveSummaryAsync(SummaryRecord summary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _summaries[summary.Id] = summary;
        return Task.CompletedTask;
    }

    public Task MarkDocumentSummarizedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _documents.AddOrUpdate(
            documentId,
            _ => throw new KeyNotFoundException($"Document {documentId} not found"),
            (_, current) => (current.Candidate, true));

        return Task.CompletedTask;
    }

    public void AddCandidate(DocumentSummaryCandidate candidate)
    {
        _documents[candidate.DocumentId] = (candidate, false);
    }

    public IReadOnlyCollection<SummaryRecord> GetSummaries()
        => _summaries.Values.ToArray();
}
