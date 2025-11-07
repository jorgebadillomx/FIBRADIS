using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Models;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Infrastructure.Observability.Metrics;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySecuritiesCacheService : ISecuritiesCacheService, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ISecuritiesRepository _repository;
    private readonly SecuritiesMetricsCollector _metrics;
    private readonly IClock _clock;
    private readonly ObservabilityMetricsRegistry _observabilityMetrics;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private CacheEntry? _cache;
    private bool _disposed;

    public InMemorySecuritiesCacheService(
        ISecuritiesRepository repository,
        SecuritiesMetricsCollector metrics,
        IClock clock,
        ObservabilityMetricsRegistry observabilityMetrics)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _observabilityMetrics = observabilityMetrics ?? throw new ArgumentNullException(nameof(observabilityMetrics));
    }

    public async Task<SecuritiesCacheResult> GetCachedAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var snapshot = Volatile.Read(ref _cache);
        var now = _clock.UtcNow;

        if (snapshot is not null && now - snapshot.StoredAt < CacheTtl)
        {
            _metrics.RecordCacheHit();
            _observabilityMetrics.RecordCacheHit();
            return snapshot.AsResult(fromCache: true);
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            snapshot = _cache;
            now = _clock.UtcNow;
            if (snapshot is not null && now - snapshot.StoredAt < CacheTtl)
            {
                _metrics.RecordCacheHit();
                _observabilityMetrics.RecordCacheHit();
                return snapshot.AsResult(fromCache: true);
            }

            var entities = await _repository.GetAllAsync(ct).ConfigureAwait(false);
            var securities = entities.Select(MapToDto).ToList();
            var json = SecuritiesJson.Serialize(securities);
            var etag = ComputeHash(json);
            snapshot = new CacheEntry(securities, json, etag, now);
            Volatile.Write(ref _cache, snapshot);
            _metrics.RecordCacheMiss();
            _observabilityMetrics.RecordCacheMiss();
            return snapshot.AsResult(fromCache: false);
        }
        finally
        {
            _sync.Release();
        }
    }

    public void InvalidateCache()
    {
        ThrowIfDisposed();
        Volatile.Write(ref _cache, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _sync.Dispose();
        _disposed = true;
    }

    private static SecurityDto MapToDto(SecurityEntity entity)
    {
        return new SecurityDto(
            entity.Ticker,
            entity.Name,
            entity.Sector,
            entity.LastPrice,
            entity.LastPriceDate,
            entity.YieldTtm,
            entity.YieldForward,
            entity.NavPerCbfi,
            entity.Ltv,
            entity.Occupancy,
            entity.UpdatedAt);
    }

    private static string ComputeHash(string payload)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return $"\"{builder}\"";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemorySecuritiesCacheService));
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(
            IReadOnlyList<SecurityDto> securities,
            string json,
            string etag,
            DateTimeOffset storedAt)
        {
            Securities = securities;
            Json = json;
            ETag = etag;
            StoredAt = storedAt;
        }

        public IReadOnlyList<SecurityDto> Securities { get; }

        public string Json { get; }

        public string ETag { get; }

        public DateTimeOffset StoredAt { get; }

        public SecuritiesCacheResult AsResult(bool fromCache)
        {
            return new SecuritiesCacheResult(Securities, fromCache, ETag, Json);
        }
    }
}
