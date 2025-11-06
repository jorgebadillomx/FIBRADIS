using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySecuritiesRepository : ISecuritiesRepository, ISecurityRepository
{
    private readonly ConcurrentDictionary<string, SecurityEntity> _securities = new(StringComparer.OrdinalIgnoreCase);
    private EventHandler? _changed;

    public InMemorySecuritiesRepository()
    {
        SeedDefaults();
    }

    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public Task<IReadOnlyList<SecurityEntity>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = _securities.Values
            .Select(Clone)
            .OrderBy(static security => security.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<SecurityEntity>>(snapshot);
    }

    public Task<SecurityEntity?> GetByTickerAsync(string ticker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker must be provided", nameof(ticker));
        }

        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_securities.TryGetValue(Normalize(ticker), out var entity)
            ? Clone(entity)
            : null);
    }

    public Task UpdateMetricsAsync(string ticker, SecurityMetricsDto metrics, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker must be provided", nameof(ticker));
        }

        if (metrics is null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        ct.ThrowIfCancellationRequested();

        var key = Normalize(ticker);
        _securities.AddOrUpdate(
            key,
            static (symbol, metricsDto) =>
            {
                return Merge(new SecurityEntity { Ticker = symbol, Name = symbol }, metricsDto);
            },
            static (_, existing, metricsDto) => Merge(existing, metricsDto),
            metrics);

        OnChanged();
        return Task.CompletedTask;
    }

    public Task UpdateYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
    {
        return UpdateMetricsAsync(
            ticker,
            new SecurityMetricsDto
            {
                YieldTtm = yieldTtm,
                YieldForward = yieldForward,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct);
    }

    public (decimal? YieldTtm, decimal? YieldForward)? GetYields(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        return _securities.TryGetValue(Normalize(ticker), out var entity)
            ? (entity.YieldTtm, entity.YieldForward)
            : null;
    }

    private static string Normalize(string ticker) => ticker.Trim().ToUpperInvariant();

    private static SecurityEntity Merge(SecurityEntity entity, SecurityMetricsDto metrics)
    {
        if (!string.IsNullOrWhiteSpace(metrics.Name))
        {
            entity.Name = metrics.Name;
        }

        if (!string.IsNullOrWhiteSpace(metrics.Sector))
        {
            entity.Sector = metrics.Sector;
        }

        if (metrics.LastPrice.HasValue)
        {
            entity.LastPrice = metrics.LastPrice.Value;
        }

        if (metrics.LastPriceDate.HasValue)
        {
            entity.LastPriceDate = metrics.LastPriceDate.Value;
        }

        if (metrics.NavPerCbfi.HasValue)
        {
            entity.NavPerCbfi = metrics.NavPerCbfi.Value;
        }

        if (metrics.Noi.HasValue)
        {
            entity.Noi = metrics.Noi.Value;
        }

        if (metrics.Affo.HasValue)
        {
            entity.Affo = metrics.Affo.Value;
        }

        if (metrics.Ltv.HasValue)
        {
            entity.Ltv = metrics.Ltv.Value;
        }

        if (metrics.Occupancy.HasValue)
        {
            entity.Occupancy = metrics.Occupancy.Value;
        }

        if (metrics.YieldTtm.HasValue)
        {
            entity.YieldTtm = metrics.YieldTtm.Value;
        }

        if (metrics.YieldForward.HasValue)
        {
            entity.YieldForward = metrics.YieldForward.Value;
        }

        if (!string.IsNullOrWhiteSpace(metrics.Source))
        {
            entity.Source = metrics.Source;
        }

        entity.UpdatedAt = metrics.UpdatedAt ?? DateTimeOffset.UtcNow;
        return entity;
    }

    private static SecurityEntity Clone(SecurityEntity entity)
    {
        return new SecurityEntity
        {
            Ticker = entity.Ticker,
            Name = entity.Name,
            Sector = entity.Sector,
            LastPrice = entity.LastPrice,
            LastPriceDate = entity.LastPriceDate,
            NavPerCbfi = entity.NavPerCbfi,
            Noi = entity.Noi,
            Affo = entity.Affo,
            Ltv = entity.Ltv,
            Occupancy = entity.Occupancy,
            YieldTtm = entity.YieldTtm,
            YieldForward = entity.YieldForward,
            Source = entity.Source,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private void OnChanged()
    {
        _changed?.Invoke(this, EventArgs.Empty);
    }

    private void SeedDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var security in GetSeedData(now))
        {
            _securities[security.Ticker] = Clone(security);
        }
    }

    private static IEnumerable<SecurityEntity> GetSeedData(DateTimeOffset referenceTime)
    {
        return new[]
        {
            new SecurityEntity
            {
                Ticker = "FUNO11",
                Name = "Fibra Uno",
                Sector = "Industrial",
                LastPrice = 25.18m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0673m,
                YieldForward = 0.0681m,
                NavPerCbfi = 30.5m,
                Ltv = 0.42m,
                Occupancy = 0.94m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRAMQ12",
                Name = "Fibra Macquarie",
                Sector = "Industrial",
                LastPrice = 21.40m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0731m,
                YieldForward = 0.0715m,
                NavPerCbfi = 26.1m,
                Ltv = 0.39m,
                Occupancy = 0.96m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "TERRA13",
                Name = "Fibra Terra",
                Sector = "Industrial",
                LastPrice = 32.55m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0652m,
                YieldForward = 0.0660m,
                NavPerCbfi = 35.9m,
                Ltv = 0.38m,
                Occupancy = 0.95m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "DANHOS13",
                Name = "Fibra Danhos",
                Sector = "Retail",
                LastPrice = 28.33m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0612m,
                YieldForward = 0.0624m,
                NavPerCbfi = 33.2m,
                Ltv = 0.36m,
                Occupancy = 0.92m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FINN13",
                Name = "Fibra Inn",
                Sector = "Hotel",
                LastPrice = 18.10m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0755m,
                YieldForward = 0.0762m,
                NavPerCbfi = 19.4m,
                Ltv = 0.41m,
                Occupancy = 0.88m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRAHD",
                Name = "Fibra HD",
                Sector = "Diversified",
                LastPrice = 12.75m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0801m,
                YieldForward = 0.0795m,
                NavPerCbfi = 14.2m,
                Ltv = 0.37m,
                Occupancy = 0.90m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRAPL14",
                Name = "Fibra Plus",
                Sector = "Office",
                LastPrice = 17.62m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0584m,
                YieldForward = 0.0590m,
                NavPerCbfi = 20.1m,
                Ltv = 0.34m,
                Occupancy = 0.85m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRAUP",
                Name = "Fibra Upsite",
                Sector = "Industrial",
                LastPrice = 28.05m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0667m,
                YieldForward = 0.0671m,
                NavPerCbfi = 29.8m,
                Ltv = 0.40m,
                Occupancy = 0.91m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRATC",
                Name = "Fibra Tasec",
                Sector = "Industrial",
                LastPrice = 23.94m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0699m,
                YieldForward = 0.0704m,
                NavPerCbfi = 25.7m,
                Ltv = 0.43m,
                Occupancy = 0.93m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FHIPO12",
                Name = "FHipo",
                Sector = "Mortgage",
                LastPrice = 9.81m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0822m,
                YieldForward = 0.0817m,
                NavPerCbfi = 10.5m,
                Ltv = 0.48m,
                Occupancy = 0.99m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRASH",
                Name = "Fibra Shop",
                Sector = "Retail",
                LastPrice = 10.72m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0644m,
                YieldForward = 0.0651m,
                NavPerCbfi = 11.9m,
                Ltv = 0.35m,
                Occupancy = 0.89m,
                UpdatedAt = referenceTime
            },
            new SecurityEntity
            {
                Ticker = "FIBRAXM",
                Name = "Fibra Xoma",
                Sector = "Industrial",
                LastPrice = 15.48m,
                LastPriceDate = referenceTime,
                YieldTtm = 0.0718m,
                YieldForward = 0.0726m,
                NavPerCbfi = 18.0m,
                Ltv = 0.44m,
                Occupancy = 0.87m,
                UpdatedAt = referenceTime
            }
        };
    }
}
