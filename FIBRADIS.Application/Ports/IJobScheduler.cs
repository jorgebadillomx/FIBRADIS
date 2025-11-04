namespace FIBRADIS.Application.Ports;

public interface IJobScheduler
{
    void EnqueuePortfolioRecalc(string userId, string reason);
}
