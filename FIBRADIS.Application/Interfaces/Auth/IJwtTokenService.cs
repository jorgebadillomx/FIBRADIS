using System.Security.Claims;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IJwtTokenService
{
    string CreateAccessToken(string userId, string username, string role);
    ClaimsPrincipal ValidateToken(string token);
}
