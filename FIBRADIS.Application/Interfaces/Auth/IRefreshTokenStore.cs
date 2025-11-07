using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IRefreshTokenStore
{
    Task SaveAsync(RefreshToken refreshToken, CancellationToken cancellationToken);
    Task<RefreshToken?> FindAsync(string token, CancellationToken cancellationToken);
    Task<bool> IsValidAsync(string token, CancellationToken cancellationToken);
    Task RevokeAsync(string token, CancellationToken cancellationToken);
    Task RotateAsync(string oldToken, RefreshToken newToken, CancellationToken cancellationToken);
}
