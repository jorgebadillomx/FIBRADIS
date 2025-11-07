using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface ISecretService
{
    Task StoreAsync(string userId, string provider, string plainTextKey, CancellationToken cancellationToken);
    Task<string?> RetrieveAsync(string userId, string provider, CancellationToken cancellationToken);
    Task RemoveAsync(string userId, string provider, CancellationToken cancellationToken);
}
