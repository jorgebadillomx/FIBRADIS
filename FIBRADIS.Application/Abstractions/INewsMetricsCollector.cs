namespace FIBRADIS.Application.Abstractions;

public interface INewsMetricsCollector
{
    void RecordInvocation();
    void RecordIngestion(TimeSpan duration, int downloaded, int duplicates, int tokensUsed, decimal costUsd);
    void RecordFailure(TimeSpan duration, string reason);
}
