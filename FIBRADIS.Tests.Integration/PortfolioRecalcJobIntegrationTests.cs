using System;
using System.Collections.Generic;
using System.Linq;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Integration;

public sealed class PortfolioRecalcJobIntegrationTests
{
    [Fact]
    public async Task UploadThenRecalc_CompletesAndAudits()
    {
        var repository = new CompositePortfolioRepository();
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 120m,
            ["FIBRATC14"] = 210m
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)>
        {
            ["FUNO11"] = (0.06m, 0.07m),
            ["FIBRATC14"] = (0.05m, 0.06m)
        });
        var scheduler = new RecordingJobScheduler();
        var clock = new FixedClock(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var correlation = new FixedCorrelationIdAccessor("integration-req");

        var replaceService = new PortfolioReplaceService(
            repository,
            securityCatalog,
            distributionReader,
            scheduler,
            clock,
            correlation,
            NullLogger<PortfolioReplaceService>.Instance);

        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 10m, AvgCost = 100m },
            new NormalizedRow { Ticker = "FIBRATC14", Qty = 5m, AvgCost = 200m }
        };

        await replaceService.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        repository.SeedValuations("user-1", new[]
        {
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), Value = 2500m },
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), Value = 2700m }
        });
        repository.SeedCashflows("user-1", new[]
        {
            new PortfolioCashflow { Timestamp = new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero), Amount = 50m }
        });

        var metricsCollector = new RecordingMetricsCollector();
        var job = new PortfolioRecalcJob(
            repository,
            securityCatalog,
            distributionReader,
            clock,
            metricsCollector,
            NullLogger<PortfolioRecalcJob>.Instance);

        await job.ExecuteAsync(new PortfolioRecalcJobInput
        {
            UserId = "user-1",
            Reason = "upload",
            RequestedAt = clock.UtcNow
        });

        Assert.Single(scheduler.EnqueuedJobs);
        var metrics = repository.GetCurrentMetrics("user-1");
        Assert.NotNull(metrics);
        Assert.Equal(2, metricsCollector.LastPositionsProcessed);
        Assert.Single(repository.MetricsHistory);
        var jobRun = Assert.Single(repository.JobRuns);
        Assert.Equal("Success", jobRun.Status);
    }

    private sealed class CompositePortfolioRepository : IPortfolioRepository
    {
        private readonly Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> _store = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> _working = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<PortfolioCashflow>> _cashflows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<PortfolioValuationSnapshot>> _valuations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PortfolioRecalcMetricsSnapshot> _currentMetrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string userId, Guid jobRunId, string reason, PortfolioRecalcMetricsSnapshot snapshot)> _metricsHistory = new();
        private readonly Dictionary<(string userId, string reason, DateOnly date), PortfolioJobRunRecord> _jobRuns = new();
        private readonly List<PortfolioJobDeadLetterRecord> _deadLetters = new();
        private bool _inTransaction;

        public Task BeginTransactionAsync(CancellationToken ct)
        {
            if (_inTransaction)
            {
                throw new InvalidOperationException("Transaction already started");
            }

            _working.Clear();
            foreach (var (user, positions) in _store)
            {
                _working[user] = positions.Select(tuple => tuple).ToList();
            }

            _inTransaction = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (!_inTransaction)
            {
                throw new InvalidOperationException("Transaction not started");
            }

            _store.Clear();
            foreach (var (user, positions) in _working)
            {
                _store[user] = positions.Select(tuple => tuple).ToList();
            }

            _inTransaction = false;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            _working.Clear();
            _inTransaction = false;
            return Task.CompletedTask;
        }

        public Task DeleteUserPortfolioAsync(string userId, CancellationToken ct)
        {
            EnsureTransaction();
            _working.Remove(userId);
            return Task.CompletedTask;
        }

        public Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct)
        {
            EnsureTransaction();
            if (!_working.TryGetValue(userId, out var list))
            {
                list = new List<(string ticker, decimal qty, decimal avgCost)>();
                _working[userId] = list;
            }

            list.AddRange(trades);
            return Task.CompletedTask;
        }

        public Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct)
        {
            EnsureTransaction();
            if (!_working.TryGetValue(userId, out var list))
            {
                return Task.FromResult(new List<(string ticker, decimal qty, decimal avgCost)>());
            }

            return Task.FromResult(Aggregate(list));
        }

        public Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct)
        {
            if (_store.TryGetValue(userId, out var list))
            {
                return Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(Aggregate(list));
            }

            return Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(Array.Empty<(string, decimal, decimal)>());
        }

        public Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioCashflow>>(_cashflows.TryGetValue(userId, out var flows) ? flows.ToList() : new List<PortfolioCashflow>());
        }

        public Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioValuationSnapshot>>(_valuations.TryGetValue(userId, out var values) ? values.ToList() : new List<PortfolioValuationSnapshot>());
        }

        public Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct)
        {
            return Task.FromResult(_jobRuns.TryGetValue((userId, reason, executionDate), out var record) ? record : null);
        }

        public Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct)
        {
            _jobRuns[(record.UserId, record.Reason, record.ExecutionDate)] = record;
            return Task.CompletedTask;
        }

        public Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct)
        {
            _currentMetrics[userId] = snapshot;
            return Task.CompletedTask;
        }

        public Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct)
        {
            _metricsHistory.Add((userId, jobRunId, reason, snapshot));
            return Task.CompletedTask;
        }

        public Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct)
        {
            _deadLetters.Add(record);
            return Task.CompletedTask;
        }

        public void SeedValuations(string userId, IEnumerable<PortfolioValuationSnapshot> snapshots)
        {
            _valuations[userId] = snapshots.OrderBy(snapshot => snapshot.AsOf).ToList();
        }

        public void SeedCashflows(string userId, IEnumerable<PortfolioCashflow> flows)
        {
            _cashflows[userId] = flows.OrderBy(flow => flow.Timestamp).ToList();
        }

        public PortfolioRecalcMetricsSnapshot? GetCurrentMetrics(string userId)
        {
            return _currentMetrics.TryGetValue(userId, out var snapshot) ? snapshot : null;
        }

        public IReadOnlyCollection<(string userId, Guid jobRunId, string reason, PortfolioRecalcMetricsSnapshot snapshot)> MetricsHistory => _metricsHistory.ToList();

        public IReadOnlyCollection<PortfolioJobRunRecord> JobRuns => _jobRuns.Values.ToList();

        public IReadOnlyCollection<PortfolioJobDeadLetterRecord> DeadLetters => _deadLetters.ToList();

        private void EnsureTransaction()
        {
            if (!_inTransaction)
            {
                throw new InvalidOperationException("Transaction required");
            }
        }

        private static List<(string ticker, decimal qty, decimal avgCost)> Aggregate(List<(string ticker, decimal qty, decimal avgCost)> entries)
        {
            return entries
                .GroupBy(entry => entry.ticker.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var qty = group.Sum(item => item.qty);
                    var invested = group.Sum(item => item.qty * item.avgCost);
                    var avgCost = qty == 0 ? 0 : invested / qty;
                    return (group.Key, qty, avgCost);
                })
                .ToList();
        }
    }

    private sealed class FakeSecurityCatalog : ISecurityCatalog
    {
        private readonly Dictionary<string, decimal?> _prices;

        public FakeSecurityCatalog(Dictionary<string, decimal?> prices)
        {
            _prices = prices;
        }

        public Task<decimal?> GetLastPriceAsync(string ticker, CancellationToken ct)
        {
            _prices.TryGetValue(ticker.ToUpperInvariant(), out var price);
            return Task.FromResult(price);
        }

        public override Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
        {
            var map = tickers
                .Select(ticker => ticker.ToUpperInvariant())
                .Distinct()
                .ToDictionary(ticker => ticker, ticker => _prices.TryGetValue(ticker, out var price) ? price : (decimal?)null, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IDictionary<string, decimal?>>(map);
        }
    }

    private sealed class FakeDistributionReader : IDistributionReader
    {
        private readonly Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)> _yields;

        public FakeDistributionReader(Dictionary<string, (decimal?, decimal?)> yields)
        {
            _yields = yields.ToDictionary(pair => pair.Key, pair => (pair.Value.Item1, pair.Value.Item2));
        }

        public Task<(decimal? YieldTtm, decimal? YieldForward)> GetYieldsAsync(string ticker, CancellationToken ct)
        {
            _yields.TryGetValue(ticker.ToUpperInvariant(), out var result);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingJobScheduler : IJobScheduler
    {
        public List<(string userId, string reason, DateTimeOffset requestedAt)> EnqueuedJobs { get; } = new();

        public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
        {
            EnqueuedJobs.Add((userId, reason, requestedAt));
        }
    }

    private sealed class RecordingMetricsCollector : IPortfolioRecalcMetricsCollector
    {
        public int InvocationCount { get; private set; }

        public int SuccessCount { get; private set; }

        public int FailureCount { get; private set; }

        public int LastPositionsProcessed { get; private set; }

        public void RecordInvocation()
        {
            InvocationCount++;
        }

        public void RecordSuccess(TimeSpan duration, int positionsProcessed, decimal? averageYield)
        {
            SuccessCount++;
            LastPositionsProcessed = positionsProcessed;
        }

        public void RecordFailure(TimeSpan duration)
        {
            FailureCount++;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FixedCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public FixedCorrelationIdAccessor(string? correlationId)
        {
            CorrelationId = correlationId;
        }

        public string? CorrelationId { get; }
    }
}
