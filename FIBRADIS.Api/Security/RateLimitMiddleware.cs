using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using Microsoft.AspNetCore.Http;

namespace FIBRADIS.Api.Security;

public sealed class RateLimitMiddleware
{
    private static readonly TimeSpan ApiWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan AdminWindow = TimeSpan.FromMinutes(1);
    private const int ApiLimit = 300;
    private const int AdminLimit = 60;

    private readonly RequestDelegate _next;
    private readonly IRateLimiterService _rateLimiterService;

    public RateLimitMiddleware(RequestDelegate next, IRateLimiterService rateLimiterService)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _rateLimiterService = rateLimiterService ?? throw new ArgumentNullException(nameof(rateLimiterService));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var role = context.User.FindFirstValue(ClaimTypes.Role) ?? "viewer";
        var key = $"{userId}:{role}";
        var window = ApiWindow;
        var limit = ApiLimit;
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            window = AdminWindow;
            limit = AdminLimit;
        }

        var allowed = await _rateLimiterService.TryConsumeAsync(key, window, limit, 1, context.RequestAborted).ConfigureAwait(false);
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "RATE_LIMIT_EXCEEDED",
                message = "Se excedió el límite de solicitudes permitido."
            });
            return;
        }

        await _next(context);
    }
}
