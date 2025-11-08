namespace FIBRADIS.Application.Abstractions;

public interface ISummarizeMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(TimeSpan duration, int documentsProcessed, int tokensUsed, decimal costUsd);
    void RecordFailure(TimeSpan duration, string reason);
}
