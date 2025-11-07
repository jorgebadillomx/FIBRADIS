using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface ILLMUsageTracker
{
    Task RecordUsageAsync(string userId, string provider, int tokens, decimal cost, CancellationToken cancellationToken);
    Task<int> GetMonthlyUsageAsync(string userId, string provider, CancellationToken cancellationToken);
}
