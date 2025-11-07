using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using Microsoft.AspNetCore.Http;

namespace FIBRADIS.Api.Security;

public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, IAuditService auditService)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _next(context);

        if (IsSensitive(path, method))
        {
            var result = context.Response.StatusCode >= 200 && context.Response.StatusCode < 400 ? "success" : "failure";
            var metadata = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["path"] = path,
                ["statusCode"] = context.Response.StatusCode
            };
            await auditService.RecordAsync(new AuditEntry(userId, $"http.{method.ToLowerInvariant()}", result, ipAddress, metadata), context.RequestAborted);
        }
    }

    private static bool IsSensitive(string path, string method)
    {
        if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Contains("portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
