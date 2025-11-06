using System.Globalization;
using System.Text;

namespace FIBRADIS.Api.Diagnostics;

public sealed class SecuritiesMetricsCollector
{
    private const int LatencyWindow = 1024;
    private long _cacheHits;
    private long _cacheMisses;
    private readonly List<double> _latencies = new();
    private readonly object _lock = new();

    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _cacheHits);
    }

    public void RecordCacheMiss()
    {
        Interlocked.Increment(ref _cacheMisses);
    }

    public void RecordLatency(TimeSpan duration)
    {
        lock (_lock)
        {
            _latencies.Add(duration.TotalSeconds);
            if (_latencies.Count > LatencyWindow)
            {
                _latencies.RemoveAt(0);
            }
        }
    }

    public string Collect()
    {
        var hits = Volatile.Read(ref _cacheHits);
        var misses = Volatile.Read(ref _cacheMisses);
        var p95 = CalculateP95();

        var builder = new StringBuilder();
        builder.AppendLine("# HELP securities_cache_hits_total Number of securities cache hits.");
        builder.AppendLine("# TYPE securities_cache_hits_total counter");
        builder.AppendLine($"securities_cache_hits_total {hits}");
        builder.AppendLine("# HELP securities_cache_miss_total Number of securities cache misses.");
        builder.AppendLine("# TYPE securities_cache_miss_total counter");
        builder.AppendLine($"securities_cache_miss_total {misses}");
        builder.AppendLine("# HELP securities_latency_p95 Securities endpoint 95th percentile latency in seconds.");
        builder.AppendLine("# TYPE securities_latency_p95 gauge");
        builder.AppendLine($"securities_latency_p95 {p95.ToString(CultureInfo.InvariantCulture)}");
        return builder.ToString();
    }

    private double CalculateP95()
    {
        lock (_lock)
        {
            if (_latencies.Count == 0)
            {
                return 0d;
            }

            var ordered = _latencies.OrderBy(static value => value).ToArray();
            var index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
            index = Math.Clamp(index, 0, ordered.Length - 1);
            return ordered[index];
        }
    }
}
