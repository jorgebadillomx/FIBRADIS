using System.Collections.Concurrent;
using System.Collections.Generic;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryFactsRepository : IFactsRepository
{
    private readonly ConcurrentDictionary<Guid, DocumentFactsRecord> _factsById = new();
    private readonly ConcurrentDictionary<Guid, DocumentFactsRecord> _factsByDocument = new();
    private readonly ConcurrentDictionary<(string Ticker, string Period), List<DocumentFactsRecord>> _factsByPeriod = new();
    private readonly ConcurrentBag<DocumentFactsRecord> _pendingReview = new();
    private readonly ConcurrentBag<FactsHistoryRecord> _history = new();

    public Task<DocumentFactsRecord?> GetDocumentFactsAsync(Guid documentId, string parserVersion, string hash, CancellationToken ct)
    {
        if (_factsByDocument.TryGetValue(documentId, out var record)
            && string.Equals(record.ParserVersion, parserVersion, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Hash, hash, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<DocumentFactsRecord?>(record);
        }

        return Task.FromResult<DocumentFactsRecord?>(null);
    }

    public Task SaveDocumentFactsAsync(DocumentFactsRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = record with { RequiresReview = false, IsSuperseded = record.IsSuperseded };

        _factsById[stored.FactId] = stored;
        _factsByDocument[stored.DocumentId] = stored;

        var key = NormalizeKey(stored.FibraTicker, stored.PeriodTag);
        _factsByPeriod.AddOrUpdate(
            key,
            _ => new List<DocumentFactsRecord> { stored },
            (_, list) =>
            {
                lock (list)
                {
                    list.RemoveAll(existing => existing.FactId == stored.FactId);
                    list.Add(stored);
                }

                return list;
            });

        return Task.CompletedTask;
    }

    public Task SavePendingReviewAsync(DocumentFactsRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = record with { RequiresReview = true };
        _pendingReview.Add(stored);
        _factsById[stored.FactId] = stored;
        _factsByDocument[stored.DocumentId] = stored;
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(FactsHistoryRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _history.Add(record);
        return Task.CompletedTask;
    }

    public Task MarkSupersededAsync(string fibraTicker, string periodTag, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fibraTicker) || string.IsNullOrWhiteSpace(periodTag))
        {
            return Task.CompletedTask;
        }

        var key = NormalizeKey(fibraTicker, periodTag);
        if (_factsByPeriod.TryGetValue(key, out var records))
        {
            lock (records)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record.IsSuperseded)
                    {
                        continue;
                    }

                    var superseded = record with { IsSuperseded = true };
                    records[i] = superseded;
                    _factsById[superseded.FactId] = superseded;
                    _factsByDocument[superseded.DocumentId] = superseded;
                }
            }
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<DocumentFactsRecord> PendingReview => _pendingReview.ToArray();

    public IReadOnlyCollection<FactsHistoryRecord> History => _history.ToArray();

    private static (string, string) NormalizeKey(string ticker, string period)
    {
        return (ticker.ToUpperInvariant(), period.ToUpperInvariant());
    }
}
