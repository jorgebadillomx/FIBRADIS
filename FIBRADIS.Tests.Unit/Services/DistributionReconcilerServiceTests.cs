using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class DistributionReconcilerServiceTests
{
    [Fact]
    public async Task ReconcileAsync_WhenOfficialMatchExists_UpdatesRecordAndYields()
    {
        var distributionId = Guid.NewGuid();
        var repository = new FakeDistributionRepository(new List<DistributionRecord>
        {
            new()
            {
                Id = distributionId,
                Ticker = "FUNO11",
                PayDate = new DateTime(2024, 3, 20),
                GrossPerCbfi = 1.00m,
                Currency = "MXN",
                Status = "imported",
                Type = "Dividend",
                Source = "Yahoo",
                Confidence = 0.5m,
                PeriodTag = "1T2024"
            }
        });
        var official = new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<OfficialDistributionRecord>
            {
                new()
                {
                    Ticker = "FUNO11",
                    PayDate = new DateTime(2024, 3, 23),
                    GrossPerCbfi = 1.02m,
                    Currency = "MXN",
                    Type = "Dividend",
                    Source = "Official"
                }
            }
        });
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 20m
        });
        var securityRepository = new FakeSecurityRepository();
        var distributionMetrics = new FakeDistributionMetricsWriter();
        var portfolioRepository = new FakePortfolioRepository(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<string> { "user-1" }
        });
        var jobScheduler = new FakeJobScheduler();
        var metricsCollector = new FakeReconcileMetricsCollector();
        var clock = new FakeClock(new DateTimeOffset(2024, 3, 25, 0, 0, 0, TimeSpan.Zero));

        var service = new DistributionReconcilerService(
            repository,
            official,
            securityCatalog,
            securityRepository,
            distributionMetrics,
            portfolioRepository,
            jobScheduler,
            metricsCollector,
            clock,
            NullLogger<DistributionReconcilerService>.Instance);

        var summary = await service.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, summary.VerifiedCount);
        var record = Assert.Single(repository.Records);
        Assert.Equal("verified", record.Status);
        Assert.Equal(new DateTime(2024, 3, 23), record.PayDate);
        Assert.Equal(1.02m, record.GrossPerCbfi);
        Assert.Equal("Dividend", record.Type);
        Assert.Equal(0.9m, record.Confidence);

        var yield = Assert.Single(distributionMetrics.Writes);
        Assert.Equal("FUNO11", yield.Ticker);
        Assert.Equal(0.051m, yield.YieldTtm);
        Assert.Equal(0.204m, yield.YieldForward);

        var updatedSecurityYield = Assert.Single(securityRepository.Updates);
        Assert.Equal("FUNO11", updatedSecurityYield.Ticker);

        var portfolioUpdate = Assert.Single(portfolioRepository.YieldUpdates);
        Assert.Equal("user-1", portfolioUpdate.UserId);
        Assert.Equal("FUNO11", portfolioUpdate.Ticker);

        var job = Assert.Single(jobScheduler.EnqueuedJobs);
        Assert.Equal("user-1", job.UserId);
        Assert.Equal("distribution", job.Reason);

        Assert.Single(metricsCollector.Results);
        Assert.Empty(metricsCollector.Failures);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSplitRequired_InsertsAdditionalRecords()
    {
        var repository = new FakeDistributionRepository(new List<DistributionRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Ticker = "FIBRAMQ12",
                PayDate = new DateTime(2024, 6, 15),
                GrossPerCbfi = 2.00m,
                Currency = "MXN",
                Status = "imported",
                Type = "Dividend",
                Source = "Yahoo",
                Confidence = 0.5m,
                PeriodTag = "2T2024"
            }
        });
        var official = new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIBRAMQ12"] = new List<OfficialDistributionRecord>
            {
                new()
                {
                    Ticker = "FIBRAMQ12",
                    PayDate = new DateTime(2024, 6, 16),
                    GrossPerCbfi = 1.20m,
                    Currency = "MXN",
                    Type = "Dividend",
                    Source = "Official"
                },
                new()
                {
                    Ticker = "FIBRAMQ12",
                    PayDate = new DateTime(2024, 6, 16),
                    GrossPerCbfi = 0.80m,
                    Currency = "MXN",
                    Type = "CapitalReturn",
                    Source = "Official"
                }
            }
        });

        var service = CreateService(repository, official);

        var summary = await service.ReconcileAsync(CancellationToken.None);

        Assert.Equal(2, summary.SplitCount);
        var records = repository.Records.Where(record => record.Ticker == "FIBRAMQ12").ToList();
        Assert.Equal(2, records.Count(record => record.Status == "verified"));
        Assert.Contains(records, record => record.Type == "Dividend" && record.GrossPerCbfi == 1.20m);
        Assert.Contains(records, record => record.Type == "CapitalReturn" && record.GrossPerCbfi == 0.80m);
    }

    [Fact]
    public async Task ReconcileAsync_WhenAmountDiffWithinTolerance_AdjustsAndVerifies()
    {
        var repository = new FakeDistributionRepository(new List<DistributionRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Ticker = "FUNO11",
                PayDate = new DateTime(2024, 4, 1),
                GrossPerCbfi = 1.00m,
                Currency = "MXN",
                Status = "imported",
                Type = "Dividend",
                Source = "Yahoo",
                Confidence = 0.5m
            }
        });
        var official = new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<OfficialDistributionRecord>
            {
                new()
                {
                    Ticker = "FUNO11",
                    PayDate = new DateTime(2024, 4, 2),
                    GrossPerCbfi = 1.02m,
                    Currency = "MXN",
                    Type = "Dividend",
                    Source = "Official"
                }
            }
        });

        var service = CreateService(repository, official);

        await service.ReconcileAsync(CancellationToken.None);

        var record = Assert.Single(repository.Records);
        Assert.Equal("verified", record.Status);
        Assert.Equal(1.02m, record.GrossPerCbfi);
    }

    [Fact]
    public async Task ReconcileAsync_WhenNoMatchFound_KeepsImported()
    {
        var repository = new FakeDistributionRepository(new List<DistributionRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Ticker = "FUNO11",
                PayDate = new DateTime(2024, 1, 1),
                GrossPerCbfi = 0.95m,
                Currency = "MXN",
                Status = "imported",
                Type = "Dividend",
                Source = "Yahoo",
                Confidence = 0.5m
            }
        });

        var service = CreateService(repository, new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>()));

        await service.ReconcileAsync(CancellationToken.None);

        var record = Assert.Single(repository.Records);
        Assert.Equal("imported", record.Status);
        Assert.Equal(0.95m, record.GrossPerCbfi);
    }

    [Fact]
    public async Task ReconcileAsync_WhenOfficialDataInvalid_MarksIgnored()
    {
        var repository = new FakeDistributionRepository(new List<DistributionRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Ticker = "FUNO11",
                PayDate = new DateTime(2024, 2, 1),
                GrossPerCbfi = 0.90m,
                Currency = "MXN",
                Status = "imported",
                Type = "Dividend",
                Source = "Yahoo",
                Confidence = 0.5m
            }
        });
        var official = new FakeOfficialDistributionSource(new Dictionary<string, IReadOnlyList<OfficialDistributionRecord>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<OfficialDistributionRecord>
            {
                new()
                {
                    Ticker = "FUNO11",
                    PayDate = new DateTime(2024, 2, 2),
                    GrossPerCbfi = -1.0m,
                    Currency = "MXN",
                    Type = "Dividend",
                    Source = "Official"
                }
            }
        });

        var service = CreateService(repository, official);

        var summary = await service.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, summary.IgnoredCount);
        var record = Assert.Single(repository.Records);
        Assert.Equal("ignored", record.Status);
    }

    private static DistributionReconcilerService CreateService(
        FakeDistributionRepository repository,
        IOfficialDistributionSource official)
    {
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 25m,
            ["FIBRAMQ12"] = 30m
        });
        var securityRepository = new FakeSecurityRepository();
        var metricsWriter = new FakeDistributionMetricsWriter();
        var portfolioRepository = new FakePortfolioRepository(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNO11"] = new List<string> { "user-1" },
            ["FIBRAMQ12"] = new List<string> { "user-2" }
        });
        var jobScheduler = new FakeJobScheduler();
        var metricsCollector = new FakeReconcileMetricsCollector();
        var clock = new FakeClock(new DateTimeOffset(2024, 6, 30, 0, 0, 0, TimeSpan.Zero));

        return new DistributionReconcilerService(
            repository,
            official,
            securityCatalog,
            securityRepository,
            metricsWriter,
            portfolioRepository,
            jobScheduler,
            metricsCollector,
            clock,
            NullLogger<DistributionReconcilerService>.Instance);
    }

    private sealed class FakeDistributionRepository : IDistributionRepository
    {
        private readonly List<DistributionRecord> _records;

        public FakeDistributionRepository(List<DistributionRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<DistributionRecord> Records => _records;

        public Task<IReadOnlyList<string>> GetActiveFibraTickersAsync(CancellationToken ct)
        {
            var tickers = _records.Select(record => record.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return Task.FromResult<IReadOnlyList<string>>(tickers);
        }

        public Task<bool> ExistsAsync(string ticker, DateTime payDate, decimal grossPerCbfi, CancellationToken ct)
        {
            var exists = _records.Any(record =>
                string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase) &&
                record.PayDate == payDate &&
                Math.Abs(record.GrossPerCbfi - grossPerCbfi) < 0.0000001m);
            return Task.FromResult(exists);
        }

        public Task InsertAsync(DistributionRecord record, CancellationToken ct)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DistributionRecord record, CancellationToken ct)
        {
            var index = _records.FindIndex(existing => existing.Id == record.Id);
            if (index >= 0)
            {
                _records[index] = record;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DistributionRecord>> GetByStatusAsync(string status, CancellationToken ct)
        {
            var result = _records.Where(record => string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult<IReadOnlyList<DistributionRecord>>(result);
        }

        public Task<IReadOnlyList<DistributionRecord>> GetVerifiedSinceAsync(string ticker, DateTime fromInclusive, CancellationToken ct)
        {
            var list = _records
                .Where(record => string.Equals(record.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.Equals(record.Status, "verified", StringComparison.OrdinalIgnoreCase))
                .Where(record => record.PayDate >= fromInclusive)
                .OrderBy(record => record.PayDate)
                .ToList();
            return Task.FromResult<IReadOnlyList<DistributionRecord>>(list);
        }
    }

    private sealed class FakeOfficialDistributionSource : IOfficialDistributionSource
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<OfficialDistributionRecord>> _official;

        public FakeOfficialDistributionSource(IReadOnlyDictionary<string, IReadOnlyList<OfficialDistributionRecord>> official)
        {
            _official = official;
        }

        public Task<IReadOnlyList<OfficialDistributionRecord>> GetOfficialDistributionsAsync(string ticker, DateTime payDate, CancellationToken ct)
        {
            return Task.FromResult(_official.TryGetValue(ticker, out var result) ? result : Array.Empty<OfficialDistributionRecord>());
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

    private sealed class FakeSecurityRepository : ISecurityRepository
    {
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> Updates { get; } = new();

        public Task UpdateYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            Updates.Add((ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDistributionMetricsWriter : IDistributionMetricsWriter
    {
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> Writes { get; } = new();

        public Task SetYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            Writes.Add((ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePortfolioRepository : IPortfolioRepository
    {
        private readonly IReadOnlyDictionary<string, List<string>> _holders;

        public FakePortfolioRepository(IReadOnlyDictionary<string, List<string>> holders)
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
            return Task.FromResult(_holders.TryGetValue(ticker, out var users)
                ? (IReadOnlyList<string>)users
                : Array.Empty<string>());
        }

        public Task UpdatePortfolioYieldMetricsAsync(string userId, string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
        {
            YieldUpdates.Add((userId, ticker, yieldTtm, yieldForward));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJobScheduler : IJobScheduler
    {
        public List<(string UserId, string Reason, DateTimeOffset RequestedAt)> EnqueuedJobs { get; } = new();

        public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
        {
            EnqueuedJobs.Add((userId, reason, requestedAt));
        }
    }

    private sealed class FakeReconcileMetricsCollector : IDistributionReconcileMetricsCollector
    {
        public List<string> Attempts { get; } = new();
        public List<(string Ticker, int Verified, int Ignored, int Split)> Results { get; } = new();
        public List<string> Failures { get; } = new();
        public List<(string Ticker, decimal? YieldTtm, decimal? YieldForward)> YieldEvents { get; } = new();

        public void RecordReconcileAttempt(string ticker)
        {
            Attempts.Add(ticker);
        }

        public void RecordReconcileResult(string ticker, int verified, int ignored, int split, TimeSpan latency)
        {
            Results.Add((ticker, verified, ignored, split));
        }

        public void RecordReconcileFailure(string ticker)
        {
            Failures.Add(ticker);
        }

        public void RecordYieldComputed(string ticker, decimal? yieldTtm, decimal? yieldForward)
        {
            YieldEvents.Add((ticker, yieldTtm, yieldForward));
        }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
