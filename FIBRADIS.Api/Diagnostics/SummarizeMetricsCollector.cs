using System;
using System.Collections.Concurrent;
using FIBRADIS.Application.Abstractions;

namespace FIBRADIS.Api.Diagnostics;

public sealed class SummarizeMetricsCollector : ISummarizeMetricsCollector
{
    private long _invocations;
    private long _failures;
    private long _documents;
    private long _tokens;
    private double _cost;
    private readonly ConcurrentQueue<double> _durations = new();

    public void RecordInvocation()
    {
        Interlocked.Increment(ref _invocations);
    }

    public void RecordSuccess(TimeSpan duration, int documentsProcessed, int tokensUsed, decimal costUsd)
    {
        Interlocked.Add(ref _documents, documentsProcessed);
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
