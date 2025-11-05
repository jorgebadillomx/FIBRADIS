using System;

namespace FIBRADIS.Application.Abstractions;

public interface IPortfolioRecalcMetricsCollector
{
    void RecordInvocation();

    void RecordSuccess(TimeSpan duration, int positionsProcessed, decimal? averageYield);

    void RecordFailure(TimeSpan duration);
}
