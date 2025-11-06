using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Integration;

public sealed class DividendsPipelineIntegrationTests
{
    [Fact]
    public async Task Pipeline_ImportAndReconcile_ProducesVerifiedDistributionsAndNotifies()
    {
        var distributionRepository = new InMemoryDistributionRepository(new[] { "FUNO11" });
        var yahooClient = new FakeYahooFinanceClient(new Dictionary<string, IReadOnlyList<YahooDividendEvent>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<YahooDividendEvent>
            {
                new() { PayDate = new DateTime(2024, 3, 15), GrossAmount = 1.10m, Currency = "MXN" }
            }
        });
        var importMetrics = new RecordingDividendsMetricsCollector();

        var importer = new DividendImporterYahoo(
            distributionRepository,
            yahooClient,
            importMetrics,
            NullLogger<DividendImporterYahoo>.Instance);

        var pullJob = new DividendsPullJob(importer, NullLogger<DividendsPullJob>.Instance);
        await pullJob.ExecuteAsync(CancellationToken.None);

        Assert.Single(distributionRepository.Records);
        var imported = distributionRepository.Records.Single();
        Assert.Equal("imported", imported.Status);
        Assert.Single(importMetrics.Success);
        Assert.Equal("FUNO11", importMetrics.Success[0].Ticker);

        var officialSource = new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<OfficialDistributionRecord>
            {
                new()
                {
                    Ticker = "FUNO11",
                    PayDate = new DateTime(2024, 3, 18),
                    GrossPerCbfi = 1.12m,
                    Currency = "MXN",
                    Type = "Dividend",
                    Source = "Official"
                }
            }
        });

        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 22m
        });
        var securityRepository = new RecordingSecurityRepository();
        var metricsWriter = new RecordingDistributionMetricsWriter();
        var portfolioRepository = new RecordingPortfolioRepository(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<string> { "user-1", "user-2" }
        });
        var jobScheduler = new RecordingJobScheduler();
        var reconcileMetrics = new RecordingReconcileMetricsCollector();
        var clock = new FixedClock(new DateTimeOffset(2024, 3, 31, 0, 0, 0, TimeSpan.Zero));

        var reconciler = new DistributionReconcilerService(
            distributionRepository,
            officialSource,
            securityCatalog,
            securityRepository,
            metricsWriter,
            portfolioRepository,
            jobScheduler,
            reconcileMetrics,
            clock,
            NullLogger<DistributionReconcilerService>.Instance);

        var reconcileJob = new DividendsReconcileJob(reconciler, NullLogger<DividendsReconcileJob>.Instance);
        await reconcileJob.ExecuteAsync(CancellationToken.None);

        var record = Assert.Single(distributionRepository.Records);
        Assert.Equal("verified", record.Status);
        Assert.Equal(new DateTime(2024, 3, 18), record.PayDate);
        Assert.Equal(1.12m, record.GrossPerCbfi);
        Assert.Equal("Dividend", record.Type);

        var yield = Assert.Single(metricsWriter.Writes);
        Assert.Equal(0.050909m, yield.YieldTtm);
        Assert.Equal(0.203636m, yield.YieldForward);

        Assert.Equal(2, portfolioRepository.YieldUpdates.Count);
        Assert.Equal(2, jobScheduler.Enqueued.Count);
        Assert.Single(reconcileMetrics.Results);
        Assert.Equal(1, reconcileMetrics.Results.Single().Verified);
        Assert.Empty(reconcileMetrics.Failures);
    }

    private sealed class InMemoryDistributionRepository : IDistributionRepository
    {
        private readonly List<string> _tickers;

        public InMemoryDistributionRepository(IEnumerable<string> tickers)
        {
            _tickers = tickers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            var index = Records.FindIndex(existing => existing.Id == record.Id);
            if (index >= 0)
            {
                Records[index] = record;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DistributionRecord>> GetByStatusAsync(string status, CancellationToken ct)
        {
            var list = Records.Where(record => string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult<IReadOnlyList<DistributionRecord>>(list);
        }

        public Task<IReadOnlyList<DistributionRecord>> GetVerifiedSinceAsync(string ticker, DateTime fromInclusive, CancellationToken ct)
        {
            var list = Records
                .Where(record => string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.Equals(record.Status, "verified", StringComparison.OrdinalIgnoreCase))
                .Where(record => record.PayDate >= fromInclusive)
                .ToList();
            return Task.FromResult<IReadOnlyList<DistributionRecord>>(list);
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
            return Task.FromResult(_events.TryGetValue(ticker, out var list) ? list : Array.Empty<YahooDividendEvent>());
        }
    }

    private sealed class FakeOfficialDistributionSource : IOfficialDistributionSource
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<OfficialDistributionRecord>> _records;

        public FakeOfficialDistributionSource(IReadOnlyDictionary<string, IReadOnlyList<OfficialDistributionRecord>> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<OfficialDistributionRecord>> GetOfficialDistributionsAsync(string ticker, DateTime payDate, CancellationToken ct)
        {
            return Task.FromResult(_records.TryGetValue(ticker, out var list) ? list : Array.Empty<OfficialDistributionRecord>());
        }
    }

    private sealed class FakeSecurityCatalog : ISecurityCatalog
    {
        private readonly IReadOnlyDictionary<string, decimal?> _prices;

        public FakeSecurityCatalog(IReadOnlyDictionary<string, decimal?> prices)
        {
            _prices = prices;
        }

        public Task<decimal?> GetLastPriceAsync(string ticker, CancellationToken ct)
        {
            return Task.FromResult(_prices.TryGetValue(ticker, out var price) ? price : (decimal?)null);
        }

        public override Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
        {
            var map = tickers.Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(ticker => ticker, ticker => _prices.TryGetValue(ticker, out var price) ? price : (decimal?)null, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IDictionary<string, decimal?>>(map);
        }
    }

    private sealed class RecordingSecurityRepository : ISecurityRepository
    {
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> Updates { get; } = new();

        public Task UpdateYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            Updates.Add((ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDistributionMetricsWriter : IDistributionMetricsWriter
    {
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> Writes { get; } = new();

        public Task SetYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            Writes.Add((ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPortfolioRepository : IPortfolioRepository
    {
        private readonly IReadOnlyDictionary<string, List<string>> _holders;

        public RecordingPortfolioRepository(IReadOnlyDictionary<string, List<string>> holders)
        {
            _holders = holders;
        }

        public List<(string UserId, string Ticker, decimal? YieldTtm, decimal? YieldForward)> YieldUpdates { get; } = new();

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public Task DeleteUserPortfolioAsync(string userId, CancellationToken ct) => Task.CompletedTask;

        public Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct) => Task.CompletedTask;

        public Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct)
            => Task.FromResult(new List<(string ticker, decimal qty, decimal avgCost)>());

        public Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(Array.Empty<(string, decimal, decimal)>());

        public Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PortfolioCashflow>>(Array.Empty<PortfolioCashflow>());

        public Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PortfolioValuationSnapshot>>(Array.Empty<PortfolioValuationSnapshot>());

        public Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct)
            => Task.FromResult<PortfolioJobRunRecord?>(null);

        public Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct) => Task.CompletedTask;

        public Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

        public Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct) => Task.CompletedTask;

        public Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetUsersHoldingTickerAsync(string ticker, CancellationToken ct)
        {
            return Task.FromResult(_holders.TryGetValue(ticker, out var list) ? (IReadOnlyList<string>)list : Array.Empty<string>());
        }

        public Task UpdatePortfolioYieldMetricsAsync(string userId, string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            YieldUpdates.Add((userId, ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingJobScheduler : IJobScheduler
    {
        public List<(string UserId, string Reason, DateTimeOffset RequestedAt)> Enqueued { get; } = new();

        public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
        {
            Enqueued.Add((userId, reason, requestedAt));
        }
    }

    private sealed class RecordingDividendsMetricsCollector : IDividendsMetricsCollector
    {
        public List<string> Attempts { get; } = new();
        public List<(string Ticker, int Imported, int Duplicates)> Success { get; } = new();
        public List<string> Failures { get; } = new();
        public List<(string Ticker, string Message)> Warnings { get; } = new();

        public void RecordPullAttempt(string ticker) => Attempts.Add(ticker);

        public void RecordPullSuccess(string ticker, int imported, int duplicates, TimeSpan latency)
            => Success.Add((ticker, imported, duplicates));

        public void RecordPullFailure(string ticker) => Failures.Add(ticker);

        public void RecordPullWarning(string ticker, string message) => Warnings.Add((ticker, message));
    }

    private sealed class RecordingReconcileMetricsCollector : IDistributionReconcileMetricsCollector
    {
        public List<string> Attempts { get; } = new();
        public List<(string Ticker, int Verified, int Ignored, int Split)> Results { get; } = new();
        public List<string> Failures { get; } = new();
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> Yields { get; } = new();

        public void RecordReconcileAttempt(string ticker) => Attempts.Add(ticker);

        public void RecordReconcileResult(string ticker, int verified, int ignored, int split, TimeSpan latency)
            => Results.Add((ticker, verified, ignored, split));

        public void RecordReconcileFailure(string ticker) => Failures.Add(ticker);

        public void RecordYieldComputed(string ticker, decimal? yieldTtm, decimal? yieldForward)
            => Yields.Add((ticker, yieldTtm, yieldForward));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
