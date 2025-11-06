using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Ports;

public interface ISecurityRepository
{
    Task UpdateYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct);
}
