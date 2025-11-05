using System;
using System.Collections.Generic;
using System.Linq;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Unit.Jobs;

public sealed class PortfolioRecalcJobTests
{
    [Fact]
    public async Task ExecuteAsync_Upload_ComputesMetricsAndPersistsResults()
    {
        var repository = new FakePortfolioRepository();
        repository.Positions.AddRange(new[]
        {
            ("FUNO11", 10m, 100m),
            ("FIBRATC14", 5m, 200m)
        });
        repository.Valuations.AddRange(new[]
        {
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2023, 12, 1, 0, 0, 0, TimeSpan.Zero), Value = 2000m },
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), Value = 2100m },
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), Value = 2300m }
        });
        repository.Cashflows.AddRange(new[]
        {
            new PortfolioCashflow { Timestamp = new DateTimeOffset(2023, 12, 15, 0, 0, 0, TimeSpan.Zero), Amount = 100m },
            new PortfolioCashflow { Timestamp = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), Amount = -50m }
        });

        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 120m,
            ["FIBRATC14"] = 220m
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)>
        {
            ["FUNO11"] = (0.06m, 0.07m),
            ["FIBRATC14"] = (0.05m, 0.06m)
        });
        var metricsCollector = new FakeMetricsCollector();
        var clock = new FakeClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));

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

        Assert.NotNull(repository.GetCurrentMetrics("user-1"));
        var jobRun = Assert.Single(repository.SavedJobRuns);
        Assert.Equal("Success", jobRun.Status);
        Assert.Equal(2, jobRun.PositionsProcessed);
        Assert.True(metricsCollector.SuccessCount > 0);
        Assert.Equal(2, metricsCollector.LastPositionsProcessed);
        Assert.NotEmpty(repository.MetricsHistory);
    }

    [Fact]
    public async Task ExecuteAsync_SkipIdempotentReason_DoesNotProcess()
    {
        var repository = new FakePortfolioRepository();
        repository.Positions.Add(("FUNO11", 1m, 100m));
        repository.SetExistingJobRun("user-1", "price", DateOnly.FromDateTime(new DateTime(2024, 2, 1)), new PortfolioJobRunRecord
        {
            JobRunId = Guid.NewGuid(),
            UserId = "user-1",
            Reason = "price",
            ExecutionDate = DateOnly.FromDateTime(new DateTime(2024, 2, 1)),
            StartedAt = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2024, 2, 1, 0, 5, 0, TimeSpan.Zero),
            Status = "Success",
            PositionsProcessed = 1,
            MetricsUpdated = true,
            Duration = TimeSpan.FromMinutes(5)
        });

        var job = new PortfolioRecalcJob(
            repository,
            new FakeSecurityCatalog(new Dictionary<string, decimal?>()),
            new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)>()),
            new FakeClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeMetricsCollector(),
            NullLogger<PortfolioRecalcJob>.Instance);

        await job.ExecuteAsync(new PortfolioRecalcJobInput
        {
            UserId = "user-1",
            Reason = "price",
            RequestedAt = DateTimeOffset.UtcNow
        });

        Assert.Empty(repository.CurrentMetrics);
        var record = Assert.Single(repository.SavedJobRuns);
        Assert.Equal("Skipped", record.Status);
        Assert.False(record.MetricsUpdated);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransientFailure_OwnsFailureMetricsAndPropagates()
    {
        var repository = new FakePortfolioRepository
        {
            PositionsException = new TimeoutException("network")
        };
        var metricsCollector = new FakeMetricsCollector();
        var job = new PortfolioRecalcJob(
            repository,
            new FakeSecurityCatalog(new Dictionary<string, decimal?>()),
            new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)>()),
            new FakeClock(DateTimeOffset.UtcNow),
            metricsCollector,
            NullLogger<PortfolioRecalcJob>.Instance);

        var input = new PortfolioRecalcJobInput
        {
            UserId = "user-1",
            Reason = "upload",
            RequestedAt = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsAsync<TimeoutException>(() => job.ExecuteAsync(input));
        Assert.True(metricsCollector.FailureCount > 0);
        Assert.Single(repository.DeadLetters);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPersistenceFails_RecordsDeadLetter()
    {
        var repository = new FakePortfolioRepository
        {
            SaveMetricsException = new InvalidOperationException("persistence failure")
        };
        repository.Positions.Add(("FUNO11", 1m, 100m));
        var job = new PortfolioRecalcJob(
            repository,
            new FakeSecurityCatalog(new Dictionary<string, decimal?> { ["FUNO11"] = 120m }),
            new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)> { ["FUNO11"] = (0.05m, 0.05m) }),
            new FakeClock(DateTimeOffset.UtcNow),
            new FakeMetricsCollector(),
            NullLogger<PortfolioRecalcJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(new PortfolioRecalcJobInput
        {
            UserId = "user-1",
            Reason = "upload",
            RequestedAt = DateTimeOffset.UtcNow
        }));

        var failure = Assert.Single(repository.DeadLetters);
        Assert.Equal("user-1", failure.UserId);
        Assert.Equal("upload", failure.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_ComputesTimeAndMoneyWeightedReturns()
    {
        var repository = new FakePortfolioRepository();
        repository.Positions.Add(("FUNO11", 10m, 100m));
        repository.Valuations.AddRange(new[]
        {
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), Value = 1000m },
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2023, 7, 1, 0, 0, 0, TimeSpan.Zero), Value = 1200m },
            new PortfolioValuationSnapshot { AsOf = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), Value = 1500m }
        });
        repository.Cashflows.AddRange(new[]
        {
            new PortfolioCashflow { Timestamp = new DateTimeOffset(2023, 3, 1, 0, 0, 0, TimeSpan.Zero), Amount = 200m },
            new PortfolioCashflow { Timestamp = new DateTimeOffset(2023, 9, 1, 0, 0, 0, TimeSpan.Zero), Amount = -100m }
        });

        var job = new PortfolioRecalcJob(
            repository,
            new FakeSecurityCatalog(new Dictionary<string, decimal?> { ["FUNO11"] = 150m }),
            new FakeDistributionReader(new Dictionary<string, (decimal?, decimal?)> { ["FUNO11"] = (0.05m, 0.05m) }),
            new FakeClock(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            new FakeMetricsCollector(),
            NullLogger<PortfolioRecalcJob>.Instance);

        await job.ExecuteAsync(new PortfolioRecalcJobInput
        {
            UserId = "user-1",
            Reason = "upload",
            RequestedAt = DateTimeOffset.UtcNow
        });

        var metrics = repository.GetCurrentMetrics("user-1");
        Assert.NotNull(metrics);
        Assert.NotNull(metrics.TimeWeightedReturn);
        Assert.NotNull(metrics.MoneyWeightedReturn);
        Assert.NotNull(metrics.AnnualizedTimeWeightedReturn);
        Assert.NotNull(metrics.AnnualizedMoneyWeightedReturn);
    }

    private sealed class FakePortfolioRepository : IPortfolioRepository
    {
        private readonly Dictionary<(string UserId, string Reason, DateOnly Date), PortfolioJobRunRecord> _jobRuns = new();

        public List<(string ticker, decimal qty, decimal avgCost)> Positions { get; } = new();

        public List<PortfolioCashflow> Cashflows { get; } = new();

        public List<PortfolioValuationSnapshot> Valuations { get; } = new();

        public List<PortfolioJobRunRecord> SavedJobRuns { get; } = new();

        public List<PortfolioRecalcMetricsSnapshot> CurrentMetrics { get; } = new();

        private readonly List<(string userId, Guid jobRunId, string reason, PortfolioRecalcMetricsSnapshot snapshot)> _metricsHistory = new();

        public List<PortfolioJobDeadLetterRecord> DeadLetters { get; } = new();

        public Exception? PositionsException { get; set; }

        public Exception? SaveMetricsException { get; set; }

        public PortfolioJobRunRecord? ExistingJobRun { get; private set; }

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public Task DeleteUserPortfolioAsync(string userId, CancellationToken ct)
        {
            Positions.Clear();
            return Task.CompletedTask;
        }

        public Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct)
        {
            Positions.AddRange(trades);
            return Task.CompletedTask;
        }

        public Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult(Positions.ToList());
        }

        public Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct)
        {
            if (PositionsException is not null)
            {
                throw PositionsException;
            }

            return Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(Positions.ToList());
        }

        public Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioCashflow>>(Cashflows.ToList());
        }

        public Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioValuationSnapshot>>(Valuations.ToList());
        }

        public Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct)
        {
            if (_jobRuns.TryGetValue((userId, reason, executionDate), out var record))
            {
                return Task.FromResult<PortfolioJobRunRecord?>(record);
            }

            return Task.FromResult<PortfolioJobRunRecord?>(ExistingJobRun);
        }

        public Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct)
        {
            _jobRuns[(record.UserId, record.Reason, record.ExecutionDate)] = record;
            SavedJobRuns.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct)
        {
            if (SaveMetricsException is not null)
            {
                throw SaveMetricsException;
            }

            CurrentMetrics.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct)
        {
            _metricsHistory.Add((userId, jobRunId, reason, snapshot));
            return Task.CompletedTask;
        }

        public Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct)
        {
            DeadLetters.Add(record);
            return Task.CompletedTask;
        }

        public void SetExistingJobRun(string userId, string reason, DateOnly date, PortfolioJobRunRecord record)
        {
            ExistingJobRun = record;
            _jobRuns[(userId, reason, date)] = record;
        }

        public PortfolioRecalcMetricsSnapshot? GetCurrentMetrics(string userId)
        {
            return CurrentMetrics.LastOrDefault();
        }

        public IReadOnlyCollection<(string userId, Guid jobRunId, string reason, PortfolioRecalcMetricsSnapshot snapshot)> MetricsHistory => _metricsHistory.ToList();
    }

    private sealed class FakeMetricsCollector : IPortfolioRecalcMetricsCollector
    {
        public int InvocationCount { get; private set; }

        public int SuccessCount { get; private set; }

        public int FailureCount { get; private set; }

        public int LastPositionsProcessed { get; private set; }

        public decimal? LastAverageYield { get; private set; }

        public void RecordInvocation()
        {
            InvocationCount++;
        }

        public void RecordSuccess(TimeSpan duration, int positionsProcessed, decimal? averageYield)
        {
            SuccessCount++;
            LastPositionsProcessed = positionsProcessed;
            LastAverageYield = averageYield;
        }

        public void RecordFailure(TimeSpan duration)
        {
            FailureCount++;
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
            _prices.TryGetValue(ticker, out var price);
            return Task.FromResult(price);
        }

        public override Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
        {
            var map = tickers
                .Select(ticker => ticker)
                .Where(ticker => ticker is not null)
                .Select(ticker => ticker!.ToUpperInvariant())
                .Distinct()
                .ToDictionary(key => key, key => _prices.TryGetValue(key, out var price) ? price : (decimal?)null, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(map);
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
            _yields.TryGetValue(ticker, out var result);
            return Task.FromResult(result);
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
