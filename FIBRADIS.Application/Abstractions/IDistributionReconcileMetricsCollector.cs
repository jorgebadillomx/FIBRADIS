using System;

namespace FIBRADIS.Application.Abstractions;

public interface IDistributionReconcileMetricsCollector
{
    void RecordReconcileAttempt(string ticker);

    void RecordReconcileResult(string ticker, int verified, int ignored, int split, TimeSpan latency);

    void RecordReconcileFailure(string ticker);

    void RecordYieldComputed(string ticker, decimal? yieldTtm, decimal? yieldForward);
}
