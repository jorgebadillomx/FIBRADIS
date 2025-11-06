using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Ports;

public interface IDistributionMetricsWriter
{
    Task SetYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct);
}
