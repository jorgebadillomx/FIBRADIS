using System;

namespace FIBRADIS.Application.Abstractions;

public interface IDividendsMetricsCollector
{
    void RecordPullAttempt(string ticker);

    void RecordPullSuccess(string ticker, int imported, int duplicates, TimeSpan latency);

    void RecordPullFailure(string ticker);

    void RecordPullWarning(string ticker, string message);
}
