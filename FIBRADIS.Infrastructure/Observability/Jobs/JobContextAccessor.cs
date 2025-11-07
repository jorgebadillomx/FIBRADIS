using System;
using System.Threading;

namespace FIBRADIS.Infrastructure.Observability.Jobs;

public sealed record JobContext(string JobRunId, string Queue, string? CorrelationId = null);

public interface IJobContextAccessor
{
    JobContext? Current { get; }
    IDisposable BeginScope(JobContext context);
}

public sealed class JobContextAccessor : IJobContextAccessor
{
    private static readonly AsyncLocal<JobContext?> CurrentContext = new();

    public JobContext? Current => CurrentContext.Value;

    public IDisposable BeginScope(JobContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly JobContext? _previous;

        public Scope(JobContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            CurrentContext.Value = _previous;
        }
    }
}
