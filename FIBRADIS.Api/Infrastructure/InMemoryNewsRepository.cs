using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryNewsRepository : INewsRepository
{
    private readonly ConcurrentDictionary<Guid, NewsRecord> _records = new();
    private readonly ConcurrentDictionary<string, Guid> _indexByHash = new(StringComparer.OrdinalIgnoreCase);

    public Task<NewsRecord?> GetByUrlHashAsync(string urlHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_indexByHash.TryGetValue(urlHash, out var id) && _records.TryGetValue(id, out var record) ? record : null);
    }

    public Task<NewsRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<NewsRecord>> GetPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = _records.Values.Where(record => record.Status == NewsStatus.Pending)
            .OrderByDescending(record => record.PublishedAt)
            .ToArray();
        return Task.FromResult<IReadOnlyList<NewsRecord>>(items);
    }

    public Task<IReadOnlyList<NewsRecord>> GetPublishedAsync(string? ticker, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _records.Values.Where(record => record.Status == NewsStatus.Published);
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            query = query.Where(record => string.Equals(record.FibraTicker, ticker, StringComparison.OrdinalIgnoreCase));
        }

        var items = query
            .OrderByDescending(record => record.PublishedAt)
            .ToArray();
        return Task.FromResult<IReadOnlyList<NewsRecord>>(items);
    }

    public Task SaveAsync(NewsRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records[record.Id] = record;
        if (!string.IsNullOrEmpty(record.UrlHash))
        {
            _indexByHash[record.UrlHash] = record.Id;
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(NewsRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records[record.Id] = record;
        if (!string.IsNullOrEmpty(record.UrlHash))
        {
            _indexByHash[record.UrlHash] = record.Id;
        }

        return Task.CompletedTask;
    }
}
