using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class NoopJobScheduler : IJobScheduler
{
    public void EnqueuePortfolioRecalc(string userId, string reason)
    {
        // No-op placeholder for future background job integration.
    }
}
