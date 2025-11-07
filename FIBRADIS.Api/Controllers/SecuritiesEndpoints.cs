using System.Diagnostics;
using System.Linq;
using System.Text;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Middleware;
using FIBRADIS.Api.Models;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Api.Controllers;

public static class SecuritiesEndpoints
{
    private const string CacheControlValue = "public, max-age=60";

    public static void MapSecuritiesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/securities", HandleGetSecuritiesAsync)
            .WithName("GetSecurities")
            .WithTags("Securities")
            .RequireAuthorization("Viewer");
    }

    internal static async Task<IResult> HandleGetSecuritiesAsync(
        HttpContext context,
        ISecuritiesCacheService cacheService,
        SecuritiesMetricsCollector metricsCollector,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (cacheService is null)
        {
            throw new ArgumentNullException(nameof(cacheService));
        }

        if (metricsCollector is null)
        {
            throw new ArgumentNullException(nameof(metricsCollector));
        }

        if (loggerFactory is null)
        {
            throw new ArgumentNullException(nameof(loggerFactory));
        }

        var logger = loggerFactory.CreateLogger("Securities");
        var requestId = ResolveRequestId(context);
        var ticker = context.Request.Query["ticker"].ToString();
        var normalizedTicker = string.IsNullOrWhiteSpace(ticker) ? null : ticker.Trim().ToUpperInvariant();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cacheResult = await cacheService.GetCachedAsync(cancellationToken).ConfigureAwait(false);
            var response = BuildResponse(cacheResult, normalizedTicker);

            if (response.Securities is null)
            {
                var notFound = CreateProblemDetails(
                    context,
                    StatusCodes.Status404NotFound,
                    "SECURITY_NOT_FOUND",
                    $"No se encontró la FIBRA {ticker?.Trim()}.",
                    requestId);
                logger.LogWarning(
                    "Security {Ticker} not found ({RequestId})",
                    normalizedTicker,
                    requestId);
                return Results.Json(notFound, statusCode: StatusCodes.Status404NotFound);
            }

            var etag = response.ETag ?? cacheResult.ETag ?? ComputeHash(response.JsonPayload);

            var isNotModified = context.Request.Headers.TryGetValue("If-None-Match", out var values)
                                && values.Any(value => string.Equals(value, etag, StringComparison.Ordinal));

            context.Response.Headers.ETag = etag;
            context.Response.Headers["Cache-Control"] = CacheControlValue;

            logger.LogInformation(
                "Securities request {RequestId} ticker={Ticker} cached={Cached} count={Count} elapsedMs={Elapsed:F2}",
                requestId,
                normalizedTicker ?? "ALL",
                cacheResult.FromCache,
                response.Count,
                stopwatch.Elapsed.TotalMilliseconds);

            if (isNotModified)
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Text(response.JsonPayload, "application/json");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error retrieving securities (ticker={Ticker}, requestId={RequestId})",
                normalizedTicker ?? "ALL",
                requestId);

            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status500InternalServerError,
                "SECURITIES_UNAVAILABLE",
                "Ocurrió un error al obtener el catálogo de FIBRAs.",
                requestId);

            return Results.Json(problem, statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            stopwatch.Stop();
            metricsCollector.RecordLatency(stopwatch.Elapsed);
        }
    }

    private static (IReadOnlyList<SecurityDto>? Securities, string JsonPayload, int Count, string? ETag) BuildResponse(
        SecuritiesCacheResult cacheResult,
        string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return (cacheResult.Securities, cacheResult.JsonPayload, cacheResult.Securities.Count, cacheResult.ETag);
        }

        var match = cacheResult.Securities
            .FirstOrDefault(security => string.Equals(security.Ticker, ticker, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return (null, string.Empty, 0, null);
        }

        var payload = new[] { match };
        var jsonPayload = SecuritiesJson.Serialize(payload);
        var etag = ComputeHash(jsonPayload);
        return (payload, jsonPayload, 1, etag);
    }

    private static string ComputeHash(string payload)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return $"\"{builder}\"";
    }

    private static string ResolveRequestId(HttpContext context)
    {
        return context.Features.Get<RequestTrackingFeature>()?.RequestId
               ?? context.Response.Headers[RequestTrackingMiddleware.RequestIdHeader].ToString()
               ?? context.TraceIdentifier
               ?? Guid.NewGuid().ToString("N");
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message,
        string requestId)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = message,
            Detail = message,
            Instance = context.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["requestId"] = requestId;

        return problem;
    }
}
