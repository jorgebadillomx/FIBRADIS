using System;
using System.Collections.Concurrent;
using FIBRADIS.Application.Abstractions;

namespace FIBRADIS.Api.Diagnostics;

public sealed class NewsMetricsCollector : INewsMetricsCollector
{
    private long _invocations;
    private long _failures;
    private long _downloads;
    private long _duplicates;
    private long _tokens;
    private double _cost;
    private readonly ConcurrentQueue<double> _durations = new();

    public void RecordInvocation()
    {
        Interlocked.Increment(ref _invocations);
    }

    public void RecordIngestion(TimeSpan duration, int downloaded, int duplicates, int tokensUsed, decimal costUsd)
    {
        Interlocked.Add(ref _downloads, downloaded);
        Interlocked.Add(ref _duplicates, duplicates);
        Interlocked.Add(ref _tokens, tokensUsed);
        AddDuration(duration);

        double currentCost;
        double updated;
        do
        {
            currentCost = Volatile.Read(ref _cost);
            updated = currentCost + (double)costUsd;
        }
        while (Math.Abs(Interlocked.CompareExchange(ref _cost, updated, currentCost) - currentCost) > double.Epsilon);
    }

    public void RecordFailure(TimeSpan duration, string reason)
    {
        Interlocked.Increment(ref _failures);
        AddDuration(duration);
    }

    private void AddDuration(TimeSpan duration)
    {
        _durations.Enqueue(duration.TotalMilliseconds);
        while (_durations.Count > 512)
        {
            _durations.TryDequeue(out _);
        }
    }
}
