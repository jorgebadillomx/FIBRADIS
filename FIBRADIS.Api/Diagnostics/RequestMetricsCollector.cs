using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FIBRADIS.Api.Diagnostics;

public sealed class RequestMetricsCollector
{
    private readonly ConcurrentDictionary<RequestMetricKey, RequestMetricValue> _metrics = new();
    private long _inFlight;

    public IDisposable TrackInFlight() => new InFlightTracker(this);

    public void Observe(string method, string path, int statusCode, TimeSpan duration)
    {
        var key = new RequestMetricKey(method, path, statusCode);
        var value = _metrics.GetOrAdd(key, _ => new RequestMetricValue());
        value.Observe(duration);
    }

    public string Collect()
    {
        var snapshot = _metrics.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# HELP fibradis_request_in_flight Number of HTTP requests currently processed.");
        builder.AppendLine("# TYPE fibradis_request_in_flight gauge");
        builder.AppendLine($"fibradis_request_in_flight {Volatile.Read(ref _inFlight)}");
        builder.AppendLine("# HELP fibradis_request_duration_seconds HTTP request duration in seconds.");
        builder.AppendLine("# TYPE fibradis_request_duration_seconds summary");

        foreach (var (key, value) in snapshot.OrderBy(entry => entry.Key.Path, StringComparer.Ordinal))
        {
            var metrics = value.Snapshot();
            var labels = $"method=\"{Escape(key.Method)}\",path=\"{Escape(key.Path)}\",status=\"{key.StatusCode}\"";
            builder.Append("fibradis_request_duration_seconds_count{").Append(labels).Append('}').Append(' ').AppendLine(metrics.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append("fibradis_request_duration_seconds_sum{").Append(labels).Append('}').Append(' ').AppendLine(metrics.SumSeconds.ToString(CultureInfo.InvariantCulture));
            builder.Append("fibradis_request_duration_seconds_max{").Append(labels).Append('}').Append(' ').AppendLine(metrics.MaxSeconds.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public RequestMetricsSnapshot CreateSnapshot()
    {
        var entries = _metrics
            .ToArray()
            .Select(pair =>
            {
                var aggregate = pair.Value.Snapshot();
                return new RequestMetricEntry(pair.Key.Method, pair.Key.Path, pair.Key.StatusCode, aggregate.Count, aggregate.SumSeconds, aggregate.MaxSeconds);
            })
            .ToArray();

        return new RequestMetricsSnapshot(Volatile.Read(ref _inFlight), entries);
    }

    public TimeSpan? TryGetAverageDuration(Func<RequestMetricEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var entries = CreateSnapshot().Entries.Where(predicate).ToArray();
        if (entries.Length == 0)
        {
            return null;
        }

        var totalCount = entries.Sum(static entry => entry.Count);
        if (totalCount == 0)
        {
            return null;
        }

        var totalSeconds = entries.Sum(static entry => entry.SumSeconds);
        var averageSeconds = totalSeconds / totalCount;
        return TimeSpan.FromSeconds(averageSeconds);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class InFlightTracker : IDisposable
    {
        private readonly RequestMetricsCollector _collector;
        private int _disposed;

        public InFlightTracker(RequestMetricsCollector collector)
        {
            _collector = collector;
            Interlocked.Increment(ref _collector._inFlight);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _collector._inFlight);
            }
        }
    }

    private sealed record RequestMetricKey(string Method, string Path, int StatusCode);

    private sealed class RequestMetricValue
    {
        private long _count;
        private double _sumSeconds;
        private double _maxSeconds;

        public void Observe(TimeSpan duration)
        {
            Interlocked.Increment(ref _count);
            var seconds = duration.TotalSeconds;

            double initialSum;
            do
            {
                initialSum = Volatile.Read(ref _sumSeconds);
            } while (Interlocked.CompareExchange(ref _sumSeconds, initialSum + seconds, initialSum) != initialSum);

            double initialMax;
            do
            {
                initialMax = Volatile.Read(ref _maxSeconds);
                if (seconds <= initialMax)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref _maxSeconds, seconds, initialMax) != initialMax);
        }

        public RequestMetricAggregate Snapshot() => new(Volatile.Read(ref _count), Volatile.Read(ref _sumSeconds), Volatile.Read(ref _maxSeconds));
    }

    private readonly record struct RequestMetricAggregate(long Count, double SumSeconds, double MaxSeconds);
}

public readonly record struct RequestMetricEntry(string Method, string Path, int StatusCode, long Count, double SumSeconds, double MaxSeconds);

public sealed record RequestMetricsSnapshot(long InFlight, IReadOnlyList<RequestMetricEntry> Entries);
