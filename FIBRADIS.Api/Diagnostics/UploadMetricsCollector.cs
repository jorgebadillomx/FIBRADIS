using System;
using System.Collections.Concurrent;

namespace FIBRADIS.Api.Diagnostics;

public sealed class UploadMetricsCollector
{
    private long _uploadCount;
    private long _uploadFailures;
    private long _uploadedBytes;
    private readonly ConcurrentQueue<double> _durations = new();

    public void RecordAttempt(long bytes)
    {
        Interlocked.Add(ref _uploadedBytes, bytes);
    }

    public void RecordSuccess(TimeSpan duration)
    {
        Interlocked.Increment(ref _uploadCount);
        RecordDuration(duration);
    }

    public void RecordFailure(TimeSpan duration)
    {
        Interlocked.Increment(ref _uploadFailures);
        RecordDuration(duration);
    }

    public UploadMetricsSnapshot Snapshot()
    {
        var durations = _durations.ToArray();
        Array.Sort(durations);
        var p95 = durations.Length == 0
            ? 0d
            : durations[(int)Math.Clamp(Math.Ceiling(durations.Length * 0.95) - 1, 0, durations.Length - 1)];

        return new UploadMetricsSnapshot(
            UploadCount: Volatile.Read(ref _uploadCount),
            FailureCount: Volatile.Read(ref _uploadFailures),
            UploadedBytes: Volatile.Read(ref _uploadedBytes),
            P95DurationMs: p95);
    }

    private void RecordDuration(TimeSpan duration)
    {
        _durations.Enqueue(duration.TotalMilliseconds);
        while (_durations.Count > 1000)
        {
            _durations.TryDequeue(out _);
        }
    }

    public readonly record struct UploadMetricsSnapshot(long UploadCount, long FailureCount, long UploadedBytes, double P95DurationMs);
}
