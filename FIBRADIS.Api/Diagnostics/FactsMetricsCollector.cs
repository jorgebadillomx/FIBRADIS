using System;
using System.Collections.Concurrent;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Infrastructure.Observability.Metrics;

namespace FIBRADIS.Api.Diagnostics;

public sealed class FactsMetricsCollector : IFactsMetricsCollector
{
    private readonly ObservabilityMetricsRegistry _observabilityMetrics;
    private long _invocations;
    private long _failures;
    private long _fieldsTotal;
    private long _scoreSamples;
    private double _scoreAverage;
    private readonly ConcurrentQueue<double> _durations = new();

    public FactsMetricsCollector(ObservabilityMetricsRegistry observabilityMetrics)
    {
        _observabilityMetrics = observabilityMetrics ?? throw new ArgumentNullException(nameof(observabilityMetrics));
    }

    public void RecordInvocation()
    {
        Interlocked.Increment(ref _invocations);
    }

    public void RecordSuccess(TimeSpan duration, int fieldsFound, int score)
    {
        Interlocked.Add(ref _fieldsTotal, fieldsFound);
        if (score >= 0)
        {
            var samples = Interlocked.Increment(ref _scoreSamples);
            double current;
            double updated;
            do
            {
                current = Volatile.Read(ref _scoreAverage);
                updated = ((current * (samples - 1)) + score) / samples;
            }
            while (Math.Abs(Interlocked.CompareExchange(ref _scoreAverage, updated, current) - current) > double.Epsilon);

            _observabilityMetrics.SetFactsScoreAverage(updated);
        }

        RecordDuration(duration);
    }

    public void RecordFailure(TimeSpan duration)
    {
        Interlocked.Increment(ref _failures);
        RecordDuration(duration);
    }

    public FactsMetricsSnapshot Snapshot()
    {
        var durations = _durations.ToArray();
        Array.Sort(durations);
        var p95 = durations.Length == 0
            ? 0d
            : durations[(int)Math.Clamp(Math.Ceiling(durations.Length * 0.95) - 1, 0, durations.Length - 1)];

        return new FactsMetricsSnapshot(
            Volatile.Read(ref _invocations),
            Volatile.Read(ref _failures),
            Volatile.Read(ref _fieldsTotal),
            Volatile.Read(ref _scoreAverage),
            p95);
    }

    private void RecordDuration(TimeSpan duration)
    {
        _durations.Enqueue(duration.TotalMilliseconds);
        while (_durations.Count > 1000)
        {
            _durations.TryDequeue(out _);
        }
    }

    public readonly record struct FactsMetricsSnapshot(
        long Invocations,
        long Failures,
        long FieldsTotal,
        double AverageScore,
        double P95DurationMs);
}
