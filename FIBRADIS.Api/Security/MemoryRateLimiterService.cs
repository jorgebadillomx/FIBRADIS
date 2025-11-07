using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;

namespace FIBRADIS.Api.Security;

public sealed class MemoryRateLimiterService : IRateLimiterService
{
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private readonly ISecurityMetricsRecorder _metrics;

    public MemoryRateLimiterService(ISecurityMetricsRecorder metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public Task<bool> TryConsumeAsync(string key, TimeSpan window, int limit, int amount, CancellationToken cancellationToken)
    {
        var bucketKey = string.Concat(key, "|", window.TotalSeconds, "|", limit);
        var bucket = _buckets.GetOrAdd(bucketKey, _ => new RateLimitBucket(DateTime.UtcNow, 0));
        lock (bucket)
        {
            var now = DateTime.UtcNow;
            if (now - bucket.WindowStart >= window)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count + amount > limit)
            {
                _metrics.RecordRateLimitBlocked();
                return Task.FromResult(false);
            }

            bucket.Count += amount;
            return Task.FromResult(true);
        }
    }

    private sealed class RateLimitBucket
    {
        public RateLimitBucket(DateTime windowStart, int count)
        {
            WindowStart = windowStart;
            Count = count;
        }

        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }
}
