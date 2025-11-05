namespace FIBRADIS.Application.Abstractions;

public interface IFactsMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(TimeSpan duration, int fieldsFound, int score);
    void RecordFailure(TimeSpan duration);
}
