using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(string username, string password, string ipAddress, CancellationToken cancellationToken);
    Task<TokenPair> RefreshAsync(string refreshToken, string ipAddress, CancellationToken cancellationToken);
    Task LogoutAsync(string refreshToken, string ipAddress, CancellationToken cancellationToken);
    ClaimsPrincipal ValidateAccessToken(string token);
}
