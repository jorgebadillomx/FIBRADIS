using System;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FIBRADIS.Api.Security;

public sealed class JwtAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtAuthMiddleware> _logger;

    public JwtAuthMiddleware(RequestDelegate next, ILogger<JwtAuthMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            var authorization = context.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization["Bearer ".Length..];
                try
                {
                    var principal = authService.ValidateAccessToken(token);
                    context.User = principal;
                }
                catch (SecurityTokenException ex)
                {
                    _logger.LogWarning(ex, "Invalid JWT token received");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error validating JWT token");
                }
            }
        }

        await _next(context);
    }
}
