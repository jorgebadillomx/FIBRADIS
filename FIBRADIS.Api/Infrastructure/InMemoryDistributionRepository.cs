using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryDistributionRepository : IDistributionRepository
{
    private readonly ConcurrentDictionary<Guid, DistributionRecord> _store = new();
    private readonly List<string> _tickers;

    public InMemoryDistributionRepository(IEnumerable<string>? tickers = null)
    {
        _tickers = tickers?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ??
            new List<string> { "FUNO11", "FIBRATC14", "FIBRAMQ12" };
    }

    public Task<IReadOnlyList<string>> GetActiveFibraTickersAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<string>>(_tickers.ToList());
    }

    public Task<bool> ExistsAsync(string ticker, DateTime payDate, decimal grossPerCbfi, CancellationToken ct)
    {
        var exists = _store.Values.Any(record =>
            string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase) &&
            record.PayDate == payDate.Date &&
            Math.Abs(record.GrossPerCbfi - grossPerCbfi) < 0.0000001m);
        return Task.FromResult(exists);
    }

    public Task InsertAsync(DistributionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _store[record.Id] = record;
        if (!_tickers.Contains(record.Ticker, StringComparer.OrdinalIgnoreCase))
        {
            _tickers.Add(record.Ticker);
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(DistributionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _store[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DistributionRecord>> GetByStatusAsync(string status, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(status);
        var list = _store.Values
            .Where(record => string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.PayDate)
            .Select(record => record)
            .ToList();
        return Task.FromResult<IReadOnlyList<DistributionRecord>>(list);
    }

    public Task<IReadOnlyList<DistributionRecord>> GetVerifiedSinceAsync(string ticker, DateTime fromInclusive, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(ticker);
        var list = _store.Values
            .Where(record => string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.Equals(record.Status, "verified", StringComparison.OrdinalIgnoreCase))
            .Where(record => record.PayDate >= fromInclusive)
            .OrderBy(record => record.PayDate)
            .Select(record => record)
            .ToList();

        return Task.FromResult<IReadOnlyList<DistributionRecord>>(list);
    }

    public IReadOnlyList<DistributionRecord> GetAll() => _store.Values.ToList();
}
