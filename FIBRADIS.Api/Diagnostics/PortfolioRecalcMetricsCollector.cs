using System;
using System.Collections.Concurrent;
using FIBRADIS.Application.Abstractions;

namespace FIBRADIS.Api.Diagnostics;

public sealed class PortfolioRecalcMetricsCollector : IPortfolioRecalcMetricsCollector
{
    private long _invocations;
    private long _failures;
    private long _positionsProcessed;
    private long _yieldSamples;
    private double _yieldAverage;
    private readonly ConcurrentQueue<double> _durations = new();

    public void RecordInvocation()
    {
        Interlocked.Increment(ref _invocations);
    }

    public void RecordSuccess(TimeSpan duration, int positionsProcessed, decimal? averageYield)
    {
        Interlocked.Add(ref _positionsProcessed, positionsProcessed);
        if (averageYield.HasValue)
        {
            var samples = Interlocked.Increment(ref _yieldSamples);
            double currentAverage;
            double updated;
            do
            {
                currentAverage = Volatile.Read(ref _yieldAverage);
                updated = ((currentAverage * (samples - 1)) + (double)averageYield.Value) / samples;
            }
            while (Interlocked.CompareExchange(ref _yieldAverage, updated, currentAverage) != currentAverage);
        }

        RecordDuration(duration);
    }

    public void RecordFailure(TimeSpan duration)
    {
        Interlocked.Increment(ref _failures);
        RecordDuration(duration);
    }

    public PortfolioRecalcMetricsSnapshot Snapshot()
    {
        var durations = _durations.ToArray();
        Array.Sort(durations);
        var p95 = durations.Length == 0
            ? 0d
            : durations[(int)Math.Clamp(Math.Ceiling(durations.Length * 0.95) - 1, 0, durations.Length - 1)];

        return new PortfolioRecalcMetricsSnapshot(
            Invocations: Volatile.Read(ref _invocations),
            Failures: Volatile.Read(ref _failures),
            PositionsProcessed: Volatile.Read(ref _positionsProcessed),
            P95DurationMs: p95,
            AverageYield: Volatile.Read(ref _yieldAverage));
    }

    private void RecordDuration(TimeSpan duration)
    {
        _durations.Enqueue(duration.TotalMilliseconds);
        while (_durations.Count > 1000)
        {
            _durations.TryDequeue(out _);
        }
    }

    public readonly record struct PortfolioRecalcMetricsSnapshot(
        long Invocations,
        long Failures,
        long PositionsProcessed,
        double P95DurationMs,
        double AverageYield);
}
