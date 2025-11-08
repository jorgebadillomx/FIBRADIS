using System;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class NoopJobScheduler : IJobScheduler
{
    public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
    {
        // No-op placeholder for future background job integration.
    }

    public void EnqueueNewsIngestion()
    {
        // No-op placeholder.
    }

    public void EnqueueSummarize(string parserVersion)
    {
        // No-op placeholder.
    }
}
