using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Tests.Unit.Infrastructure;

public class InMemorySecuritiesCacheServiceTests
{
    [Fact]
    public async Task GetAll_ReturnsCachedList()
    {
        var repository = new CountingSecuritiesRepository();
        var metrics = new SecuritiesMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var cache = new InMemorySecuritiesCacheService(repository, metrics, clock);

        _ = await cache.GetCachedAsync(CancellationToken.None);
        _ = await cache.GetCachedAsync(CancellationToken.None);

        Assert.Equal(1, repository.InvocationCount);
    }

    [Fact]
    public async Task Cache_Invalidated_After_Update()
    {
        var repository = new InMemorySecuritiesRepository();
        var metrics = new SecuritiesMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var cache = new InMemorySecuritiesCacheService(repository, metrics, clock);
        repository.Changed += (_, _) => cache.InvalidateCache();

        var initial = await cache.GetCachedAsync(CancellationToken.None);
        var target = initial.Securities.First();

        await repository.UpdateMetricsAsync(
            target.Ticker,
            new SecurityMetricsDto
            {
                YieldTtm = 0.5m,
                UpdatedAt = clock.UtcNow + TimeSpan.FromSeconds(5)
            },
            CancellationToken.None);

        var refreshed = await cache.GetCachedAsync(CancellationToken.None);
        var updated = refreshed.Securities.First(security => security.Ticker == target.Ticker);

        Assert.Equal(0.5m, updated.YieldTtm);
        Assert.NotEqual(initial.ETag, refreshed.ETag);
    }

    [Fact]
    public async Task ETag_Changes_When_Data_Changes()
    {
        var repository = new InMemorySecuritiesRepository();
        var metrics = new SecuritiesMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var cache = new InMemorySecuritiesCacheService(repository, metrics, clock);
        repository.Changed += (_, _) => cache.InvalidateCache();

        var initial = await cache.GetCachedAsync(CancellationToken.None);

        await repository.UpdateMetricsAsync(
            "FUNO11",
            new SecurityMetricsDto
            {
                LastPrice = 99.99m,
                UpdatedAt = clock.UtcNow + TimeSpan.FromSeconds(10)
            },
            CancellationToken.None);

        var refreshed = await cache.GetCachedAsync(CancellationToken.None);

        Assert.NotEqual(initial.ETag, refreshed.ETag);
    }

    private sealed class CountingSecuritiesRepository : ISecuritiesRepository
    {
        private readonly IReadOnlyList<SecurityEntity> _securities;

        public CountingSecuritiesRepository()
        {
            _securities = new List<SecurityEntity>
            {
                new()
                {
                    Ticker = "TEST1",
                    Name = "Test Fibra",
                    Sector = "Industrial",
                    LastPrice = 10m,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            };
        }

        public int InvocationCount { get; private set; }

        public Task<IReadOnlyList<SecurityEntity>> GetAllAsync(CancellationToken ct)
        {
            InvocationCount++;
            return Task.FromResult(_securities);
        }

        public Task<SecurityEntity?> GetByTickerAsync(string ticker, CancellationToken ct)
        {
            return Task.FromResult<SecurityEntity?>(_securities.FirstOrDefault(security => security.Ticker == ticker));
        }

        public Task UpdateMetricsAsync(string ticker, SecurityMetricsDto metrics, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset initial)
        {
            UtcNow = initial;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }
}
