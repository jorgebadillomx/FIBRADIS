using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IRateLimiterService
{
    Task<bool> TryConsumeAsync(string key, TimeSpan window, int limit, int amount, CancellationToken cancellationToken);
}
