using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IUserStore
{
    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<UserAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken);
}
