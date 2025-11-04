using System;
using System.Collections.Generic;
using System.Linq;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Integration;

public sealed class PortfolioReplaceServiceIntegrationTests
{
    [Fact]
    public async Task ReplaceAsync_EndToEnd_ReplacesPortfolioAndCalculatesMetrics()
    {
        var repository = new InMemoryPortfolioRepository();
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 110m,
            ["FIBRAMQ12"] = 180m
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>
        {
            ["FUNO11"] = (0.05m, 0.06m),
            ["FIBRAMQ12"] = (0.04m, 0.05m)
        });
        var jobScheduler = new TestJobScheduler();
        var service = new PortfolioReplaceService(
            repository,
            securityCatalog,
            distributionReader,
            jobScheduler,
            new TestClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new TestCorrelationIdAccessor("req-end2end"),
            NullLogger<PortfolioReplaceService>.Instance);

        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 10, AvgCost = 100 },
            new NormalizedRow { Ticker = "FIBRAMQ12", Qty = 5, AvgCost = 200 }
        };

        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        Assert.Equal(2, response.Positions.Count);
        Assert.Equal(2, response.Imported);
        Assert.Equal("req-end2end", response.RequestId);
        Assert.Equal(2000m, response.Metrics.Invested);
        Assert.Equal(2000m, response.Metrics.Value);
        Assert.Equal(0.0455m, response.Metrics.YieldTtm);
        Assert.Equal(0.055m, response.Metrics.YieldForward);

        var job = Assert.Single(jobScheduler.EnqueuedJobs);
        Assert.Equal("user-1", job.userId);
        Assert.Equal("upload", job.reason);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), job.requestedAt);

        var storedPositions = repository.GetCommittedPositions("user-1");
        Assert.Equal(2, storedPositions.Count);
    }

    [Fact]
    public async Task ReplaceAsync_WhenPriceMissing_ReturnsZeroValueAndNegativePnl()
    {
        var repository = new InMemoryPortfolioRepository();
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = null
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>
        {
            ["FUNO11"] = (0.08m, 0.09m)
        });
        var jobScheduler = new TestJobScheduler();
        var service = new PortfolioReplaceService(
            repository,
            securityCatalog,
            distributionReader,
            jobScheduler,
            new TestClock(DateTimeOffset.UtcNow),
            new TestCorrelationIdAccessor("req-missing-price"),
            NullLogger<PortfolioReplaceService>.Instance);

        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 3, AvgCost = 120 }
        };

        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        var position = Assert.Single(response.Positions);
        Assert.Equal(0m, position.Value);
        Assert.Equal(-360m, position.Pnl);
        Assert.Equal(0m, response.Metrics.Value);
        Assert.Equal(-360m, response.Metrics.Pnl);
        Assert.Null(response.Metrics.YieldTtm);
        Assert.Null(response.Metrics.YieldForward);
    }

    [Fact]
    public async Task ReplaceAsync_WhenYieldsProvided_ComputesWeightedTotals()
    {
        var repository = new InMemoryPortfolioRepository();
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 150m,
            ["FIBRAMQ12"] = 180m
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>
        {
            ["FUNO11"] = (0.12m, 0.10m),
            ["FIBRAMQ12"] = (null, 0.08m)
        });
        var jobScheduler = new TestJobScheduler();
        var service = new PortfolioReplaceService(
            repository,
            securityCatalog,
            distributionReader,
            jobScheduler,
            new TestClock(DateTimeOffset.UtcNow),
            new TestCorrelationIdAccessor("req-yields"),
            NullLogger<PortfolioReplaceService>.Instance);

        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 2, AvgCost = 100 },
            new NormalizedRow { Ticker = "FIBRAMQ12", Qty = 3, AvgCost = 150 }
        };

        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        Assert.Equal(2, response.Positions.Count);
        Assert.Equal(1, response.Positions.Count(position => position.YieldTtm.HasValue));
        Assert.Equal(0.12m, response.Metrics.YieldTtm);
        Assert.Equal(0.087143m, response.Metrics.YieldForward);
    }

    [Fact]
    public async Task ReplaceAsync_WithConcurrentRequests_SerializesTransactions()
    {
        var repository = new InMemoryPortfolioRepository();
        var securityCatalog = new FakeSecurityCatalog(new Dictionary<string, decimal?>
        {
            ["FUNO11"] = 100m,
            ["FIBRAMQ12"] = 200m
        });
        var distributionReader = new FakeDistributionReader(new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>());
        var jobScheduler = new TestJobScheduler();
        var service = new PortfolioReplaceService(
            repository,
            securityCatalog,
            distributionReader,
            jobScheduler,
            new TestClock(DateTimeOffset.UtcNow),
            new TestCorrelationIdAccessor("req-concurrent"),
            NullLogger<PortfolioReplaceService>.Instance);

        var task1 = service.ReplaceAsync(
            "user-1",
            new[] { new NormalizedRow { Ticker = "FUNO11", Qty = 1, AvgCost = 90 } },
            Array.Empty<ValidationIssue>(),
            CancellationToken.None);
        var task2 = service.ReplaceAsync(
            "user-1",
            new[] { new NormalizedRow { Ticker = "FIBRAMQ12", Qty = 2, AvgCost = 190 } },
            Array.Empty<ValidationIssue>(),
            CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(2, jobScheduler.EnqueuedJobs.Count);
        Assert.All(results, result => Assert.Equal(1, result.Imported));

        var finalPositions = repository.GetCommittedPositions("user-1");
        Assert.Single(finalPositions);
        Assert.True(finalPositions[0].ticker is "FUNO11" or "FIBRAMQ12");
    }

    private sealed class InMemoryPortfolioRepository : IPortfolioRepository
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> _store = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>>? _working;

        public async Task BeginTransactionAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            _working = CloneStore(_store);
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (_working is null)
            {
                throw new InvalidOperationException("Transaction has not been started.");
            }

            _store = _working;
            _working = null;
            _gate.Release();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            _working = null;
            _gate.Release();
            return Task.CompletedTask;
        }

        public Task DeleteUserPortfolioAsync(string userId, CancellationToken ct)
        {
            EnsureWorking();
            _working![userId] = new List<(string ticker, decimal qty, decimal avgCost)>();
            return Task.CompletedTask;
        }

        public Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct)
        {
            EnsureWorking();
            if (!_working!.TryGetValue(userId, out var list))
            {
                list = new List<(string ticker, decimal qty, decimal avgCost)>();
                _working[userId] = list;
            }

            list.AddRange(trades);
            return Task.CompletedTask;
        }

        public Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct)
        {
            EnsureWorking();
            if (!_working!.TryGetValue(userId, out var list) || list.Count == 0)
            {
                return Task.FromResult(new List<(string ticker, decimal qty, decimal avgCost)>());
            }

            var aggregated = list
                .GroupBy(entry => entry.ticker.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var qty = group.Sum(item => item.qty);
                    var invested = group.Sum(item => item.qty * item.avgCost);
                    var avgCost = qty == 0 ? 0 : invested / qty;
                    return (group.Key, qty, avgCost);
                })
                .ToList();

            return Task.FromResult(aggregated);
        }

        public Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(GetCommittedPositions(userId));
        }

        public Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioCashflow>>(Array.Empty<PortfolioCashflow>());
        }

        public Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<PortfolioValuationSnapshot>>(Array.Empty<PortfolioValuationSnapshot>());
        }

        public Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct)
        {
            return Task.FromResult<PortfolioJobRunRecord?>(null);
        }

        public Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public List<(string ticker, decimal qty, decimal avgCost)> GetCommittedPositions(string userId)
        {
            if (!_store.TryGetValue(userId, out var list))
            {
                return new List<(string ticker, decimal qty, decimal avgCost)>();
            }

            return list
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

        private static Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> CloneStore(Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> source)
        {
            var clone = new Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>>(source.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in source)
            {
                clone[key] = new List<(string ticker, decimal qty, decimal avgCost)>(value);
            }

            return clone;
        }

        private void EnsureWorking()
        {
            if (_working is null)
            {
                throw new InvalidOperationException("Transaction has not been started.");
            }
        }
    }

    private sealed class FakeSecurityCatalog : ISecurityCatalog
    {
        private readonly Dictionary<string, decimal?> _prices;

        public bool ThrowOnBatch { get; set; }

        public FakeSecurityCatalog(Dictionary<string, decimal?> prices)
        {
            _prices = prices;
        }

        public Task<decimal?> GetLastPriceAsync(string ticker, CancellationToken ct)
        {
            _prices.TryGetValue(ticker.ToUpperInvariant(), out var price);
            return Task.FromResult(price);
        }

        public Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
        {
            if (ThrowOnBatch)
            {
                throw new NotSupportedException();
            }

            var result = tickers
                .Select(ticker => ticker.Trim().ToUpperInvariant())
                .Distinct()
                .ToDictionary(ticker => ticker, ticker => _prices.TryGetValue(ticker, out var price) ? price : (decimal?)null, StringComparer.OrdinalIgnoreCase);

            return Task.FromResult<IDictionary<string, decimal?>>(result);
        }
    }

    private sealed class FakeDistributionReader : IDistributionReader
    {
        private readonly Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)> _yields;

        public FakeDistributionReader(Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)> yields)
        {
            _yields = yields;
        }

        public Task<(decimal? YieldTtm, decimal? YieldForward)> GetYieldsAsync(string ticker, CancellationToken ct)
        {
            _yields.TryGetValue(ticker.ToUpperInvariant(), out var result);
            return Task.FromResult(result);
        }
    }

    private sealed class TestJobScheduler : IJobScheduler
    {
        public List<(string userId, string reason, DateTimeOffset requestedAt)> EnqueuedJobs { get; } = new();

        public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
        {
            EnqueuedJobs.Add((userId, reason, requestedAt));
        }
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public TestCorrelationIdAccessor(string? correlationId)
        {
            CorrelationId = correlationId;
        }

        public string? CorrelationId { get; }
    }
}
