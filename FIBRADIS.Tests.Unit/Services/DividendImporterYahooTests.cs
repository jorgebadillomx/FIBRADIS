using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class DividendImporterYahooTests
{
    [Fact]
    public async Task ImportAsync_WithValidData_PersistsRecordsAndEmitsMetrics()
    {
        var repository = new FakeDistributionRepository(new[] { "FUNO11" });
        var client = new FakeYahooFinanceClient(new Dictionary<string, IReadOnlyList<YahooDividendEvent>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<YahooDividendEvent>
            {
                new() { PayDate = new DateTime(2024, 3, 20), GrossAmount = 1.23m, Currency = "MXN" },
                new() { PayDate = new DateTime(2024, 6, 20), GrossAmount = 1.30m, Currency = "MXN" }
            }
        });
        var metrics = new FakeDividendsMetricsCollector();

        var importer = new DividendImporterYahoo(
            repository,
            client,
            metrics,
            NullLogger<DividendImporterYahoo>.Instance);

        var summary = await importer.ImportAsync(CancellationToken.None);

        Assert.Equal(2, summary.CountImported);
        Assert.Equal(0, summary.CountDuplicates);
        Assert.Empty(summary.Warnings);
        Assert.Equal(0, summary.CountFailed);

        var records = repository.Records;
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal("FUNO11", record.Ticker));
        Assert.All(records, record => Assert.Equal("Yahoo", record.Source));
        Assert.All(records, record => Assert.Equal(0.5m, record.Confidence));
        Assert.Contains(records, record => record.PayDate == new DateTime(2024, 3, 20));
        Assert.Contains(records, record => record.PayDate == new DateTime(2024, 6, 20));
        Assert.Equal(2, metrics.Success.Count);
        Assert.Equal("FUNO11", Assert.Single(metrics.Attempts));
    }

    private sealed class FakeDistributionRepository : IDistributionRepository
    {
        private readonly List<string> _tickers;

        public FakeDistributionRepository(IEnumerable<string> tickers)
        {
            _tickers = tickers.ToList();
        }

        public List<DistributionRecord> Records { get; } = new();

        public Task<IReadOnlyList<string>> GetActiveFibraTickersAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<string>>(_tickers);
        }

        public Task<bool> ExistsAsync(string ticker, DateTime payDate, decimal grossPerCbfi, CancellationToken ct)
        {
            var exists = Records.Any(record =>
                string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase) &&
                record.PayDate == payDate &&
                Math.Abs(record.GrossPerCbfi - grossPerCbfi) < 0.0000001m);
            return Task.FromResult(exists);
        }

        public Task InsertAsync(DistributionRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DistributionRecord record, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<DistributionRecord>> GetByStatusAsync(string status, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<DistributionRecord>> GetVerifiedSinceAsync(string ticker, DateTime fromInclusive, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeYahooFinanceClient : IYahooFinanceClient
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<YahooDividendEvent>> _events;

        public FakeYahooFinanceClient(IReadOnlyDictionary<string, IReadOnlyList<YahooDividendEvent>> events)
        {
            _events = events;
        }

        public Task<IReadOnlyList<YahooDividendEvent>> GetDividendSeriesAsync(string ticker, CancellationToken ct)
        {
            return Task.FromResult(_events.TryGetValue(ticker, out var result) ? result : Array.Empty<YahooDividendEvent>());
        }
    }

    private sealed class FakeDividendsMetricsCollector : IDividendsMetricsCollector
    {
        public List<string> Attempts { get; } = new();
        public List<(string Ticker, int Imported, int Duplicates)> Success { get; } = new();
        public List<string> Failures { get; } = new();
        public List<(string Ticker, string Message)> Warnings { get; } = new();

        public void RecordPullAttempt(string ticker)
        {
            Attempts.Add(ticker);
        }

        public void RecordPullSuccess(string ticker, int imported, int duplicates, TimeSpan latency)
        {
            Success.Add((ticker, imported, duplicates));
        }

        public void RecordPullFailure(string ticker)
        {
            Failures.Add(ticker);
        }

        public void RecordPullWarning(string ticker, string message)
        {
            Warnings.Add((ticker, message));
        }
    }
}
