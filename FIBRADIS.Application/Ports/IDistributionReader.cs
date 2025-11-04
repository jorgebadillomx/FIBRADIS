namespace FIBRADIS.Application.Ports;

public interface IDistributionReader
{
    Task<(decimal? YieldTtm, decimal? YieldForward)> GetYieldsAsync(string ticker, CancellationToken ct);
}
