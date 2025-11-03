using System.Diagnostics;
using FIBRADIS.Api.Diagnostics;

namespace FIBRADIS.Api.Middleware;

public sealed class RequestTrackingMiddleware
{
    public const string RequestIdHeader = "X-Request-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTrackingMiddleware> _logger;
    private readonly RequestMetricsCollector _metrics;

    public RequestTrackingMiddleware(RequestDelegate next, ILogger<RequestTrackingMiddleware> logger, RequestMetricsCollector metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = ResolveRequestId(context);
        context.Features.Set(new RequestTrackingFeature(requestId));
        context.Response.Headers[RequestIdHeader] = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestIdHeader] = requestId;
            return Task.CompletedTask;
        });

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [nameof(RequestTrackingFeature.RequestId)] = requestId,
            ["Path"] = context.Request.Path.ToString(),
            ["Method"] = context.Request.Method
        });

        using var inFlight = _metrics.TrackInFlight();

        _logger.LogInformation("Handling HTTP {Method} {Path}", context.Request.Method, context.Request.Path);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.Observe(context.Request.Method, context.Request.Path.ToString(), context.Response.StatusCode, stopwatch.Elapsed);
            _logger.LogInformation("Completed HTTP {Method} {Path} with {StatusCode} in {ElapsedMs} ms", context.Request.Method, context.Request.Path, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static string ResolveRequestId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var incoming) && !string.IsNullOrWhiteSpace(incoming))
        {
            return incoming.ToString();
        }

        return context.TraceIdentifier ?? Guid.NewGuid().ToString("N");
    }
}

public sealed record RequestTrackingFeature(string RequestId);
