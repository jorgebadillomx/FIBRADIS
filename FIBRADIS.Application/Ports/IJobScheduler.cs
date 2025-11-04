namespace FIBRADIS.Application.Ports;

using System;

public interface IJobScheduler
{
    void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt);
}
