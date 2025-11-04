using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FIBRADIS.Api.Authentication;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Api.Middleware;
using FIBRADIS.Api.Models;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Development", policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin());
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<RequestMetricsCollector>();
builder.Services.AddSingleton<UploadMetricsCollector>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();
builder.Services.AddSingleton<IPortfolioFileParser, PortfolioFileParser>();
builder.Services.AddSingleton<IPortfolioReplaceService, PortfolioReplaceService>();
builder.Services.AddSingleton<IPortfolioRepository, InMemoryPortfolioRepository>();
builder.Services.AddSingleton<ISecurityCatalog, InMemorySecurityCatalog>();
builder.Services.AddSingleton<IDistributionReader, InMemoryDistributionReader>();
builder.Services.AddSingleton<IJobScheduler, NoopJobScheduler>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = FakeJwtAuthenticationHandler.SchemeName;
    options.DefaultChallengeScheme = FakeJwtAuthenticationHandler.SchemeName;
}).AddScheme<AuthenticationSchemeOptions, FakeJwtAuthenticationHandler>(FakeJwtAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserOrHigher", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var role = context.User.FindFirstValue(ClaimTypes.Role);
            return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "superuser", StringComparison.OrdinalIgnoreCase);
        });
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, token) =>
    {
        var requestId = context.HttpContext.Features.Get<RequestTrackingFeature>()?.RequestId
                        ?? context.HttpContext.TraceIdentifier
                        ?? Guid.NewGuid().ToString("N");

        var metrics = context.HttpContext.RequestServices.GetRequiredService<UploadMetricsCollector>();
        metrics.RecordFailure(TimeSpan.Zero);

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var problem = CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "RATE_LIMIT_EXCEEDED",
            "Se excedió el límite de cargas permitido.",
            requestId);

        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), token).ConfigureAwait(false);
    };

    options.AddPolicy("UploadRatePolicy", httpContext =>
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId!;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}

app.UseMiddleware<RequestTrackingMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/v1/ping", (HttpContext context) =>
{
    var requestId = context.Features.Get<RequestTrackingFeature>()?.RequestId
                    ?? context.Response.Headers[RequestTrackingMiddleware.RequestIdHeader].ToString()
                    ?? context.TraceIdentifier
                    ?? Guid.NewGuid().ToString("N");

    return Results.Json(new PingResponse("pong", requestId));
});

app.MapPost("/v1/portfolio/upload",
    async Task<IResult> (
        HttpContext context,
        IPortfolioFileParser parser,
        IPortfolioReplaceService replaceService,
        UploadMetricsCollector uploadMetrics,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        const long MaxFileSizeBytes = 2 * 1024 * 1024;
        var logger = loggerFactory.CreateLogger("PortfolioUpload");
        var requestId = context.Features.Get<RequestTrackingFeature>()?.RequestId
                        ?? context.TraceIdentifier
                        ?? Guid.NewGuid().ToString("N");

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            return Results.Unauthorized();
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            var unauthorizedProblem = CreateProblemDetails(
                context,
                StatusCodes.Status401Unauthorized,
                "USER_ID_MISSING",
                "El token no contiene un identificador de usuario.",
                requestId);
            return Results.Json(unauthorizedProblem, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!context.Request.HasFormContentType)
        {
            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status400BadRequest,
                "PORTFOLIO_FILE_MISSING",
                "Debe proporcionar un archivo de portafolio.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status400BadRequest,
                "PORTFOLIO_FILE_MISSING",
                "Debe proporcionar un archivo de portafolio.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length == 0)
        {
            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status400BadRequest,
                "PORTFOLIO_FILE_EMPTY",
                "El archivo recibido está vacío.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxFileSizeBytes)
        {
            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "PORTFOLIO_FILE_TOO_LARGE",
                "El archivo excede el tamaño máximo permitido de 2 MB.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || extension is not (".csv" or ".xlsx"))
        {
            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "PORTFOLIO_FILE_UNSUPPORTED",
                "Solo se permiten archivos .csv o .xlsx.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        await using var fileStream = file.OpenReadStream(MaxFileSizeBytes);
        await using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        memoryStream.Position = 0;

        uploadMetrics.RecordAttempt(memoryStream.Length);

        var hashBytes = SHA256.HashData(memoryStream.ToArray());
        var fileHash = Convert.ToHexString(hashBytes);
        memoryStream.Position = 0;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (rows, issues) = await parser.ParseAsync(memoryStream, file.FileName, cancellationToken).ConfigureAwait(false);
            var materializedRows = rows?.Where(row => row is not null).ToList() ?? new List<NormalizedRow>();
            var issueList = issues?.ToList() ?? new List<ValidationIssue>();

            var totalIssues = issueList.Count;
            var errorIssues = issueList.Count(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
            var warningIssues = totalIssues - errorIssues;

            logger.LogInformation(
                "Upload request received for {UserId} ({RequestId}). File={File}, SizeBytes={Size}, Hash={Hash}, Issues={Issues}, Errors={Errors}, Warnings={Warnings}",
                userId,
                requestId,
                file.FileName,
                memoryStream.Length,
                fileHash,
                totalIssues,
                errorIssues,
                warningIssues);

            if (materializedRows.Count == 0)
            {
                stopwatch.Stop();
                uploadMetrics.RecordFailure(stopwatch.Elapsed);

                var problem = CreateProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "PORTFOLIO_NO_VALID_ROWS",
                    "No se encontraron filas válidas en el archivo.",
                    requestId,
                    new Dictionary<string, object?>
                    {
                        ["issues"] = issueList
                    });

                return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest);
            }

            var response = await replaceService.ReplaceAsync(userId, materializedRows, issueList, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            {
                response = response with { RequestId = requestId };
            }

            stopwatch.Stop();
            uploadMetrics.RecordSuccess(stopwatch.Elapsed);

            logger.LogInformation(
                "Upload completed for {UserId} ({RequestId}). Imported={Imported}, Ignored={Ignored}, Errors={Errors}, DurationMs={Elapsed}",
                userId,
                requestId,
                response.Imported,
                response.Ignored,
                response.Errors,
                stopwatch.Elapsed.TotalMilliseconds);

            logger.LogInformation(
                "portfolio.upload.replace audit: userId={UserId}, positions={Positions}, fileHash={Hash}, ip={Ip}, requestId={RequestId}",
                userId,
                response.Positions.Count,
                fileHash,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                requestId);

            return Results.Ok(response);
        }
        catch (UnsupportedFormatException ex)
        {
            stopwatch.Stop();
            uploadMetrics.RecordFailure(stopwatch.Elapsed);
            logger.LogWarning(ex, "Formato de archivo no soportado para {UserId} ({RequestId})", userId, requestId);

            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "PORTFOLIO_FILE_UNSUPPORTED",
                ex.Message,
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }
        catch (InvalidDataException ex)
        {
            stopwatch.Stop();
            uploadMetrics.RecordFailure(stopwatch.Elapsed);
            logger.LogWarning(ex, "Archivo inválido para {UserId} ({RequestId})", userId, requestId);

            var statusCode = ex.Message.Contains("excede", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;
            var errorCode = statusCode == StatusCodes.Status413PayloadTooLarge
                ? "PORTFOLIO_FILE_TOO_LARGE"
                : "PORTFOLIO_FILE_INVALID";

            var problem = CreateProblemDetails(
                context,
                statusCode,
                errorCode,
                ex.Message,
                requestId);
            return Results.Json(problem, statusCode: statusCode);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            uploadMetrics.RecordFailure(stopwatch.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            uploadMetrics.RecordFailure(stopwatch.Elapsed);
            logger.LogError(ex, "Error procesando upload para {UserId} ({RequestId})", userId, requestId);

            var problem = CreateProblemDetails(
                context,
                StatusCodes.Status500InternalServerError,
                "PORTFOLIO_UPLOAD_FAILED",
                "Ocurrió un error al procesar el portafolio.",
                requestId);
            return Results.Json(problem, statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .RequireAuthorization("UserOrHigher")
    .RequireRateLimiting("UploadRatePolicy");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (httpContext, report) =>
    {
        httpContext.Response.ContentType = "application/json";
        var response = HealthReportModel.FromReport(report);
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

app.MapGet("/metrics", (RequestMetricsCollector collector) =>
{
    return Results.Text(collector.Collect(), "text/plain; version=0.0.4; charset=utf-8");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Application started in {Environment} mode", app.Environment.EnvironmentName);
});

app.Run();

public partial class Program
{
}

static ProblemDetails CreateProblemDetails(
    HttpContext context,
    int statusCode,
    string errorCode,
    string message,
    string requestId,
    IDictionary<string, object?>? extensions = null)
{
    var problem = new ProblemDetails
    {
        Status = statusCode,
        Title = message,
        Detail = message,
        Instance = context.Request.Path
    };

    problem.Extensions["requestId"] = requestId;
    problem.Extensions["errorCode"] = errorCode;

    if (extensions is not null)
    {
        foreach (var pair in extensions)
        {
            problem.Extensions[pair.Key] = pair.Value;
        }
    }

    return problem;
}
