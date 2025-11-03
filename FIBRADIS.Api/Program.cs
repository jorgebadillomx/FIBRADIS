using System.Text.Json;
using System.Text.Json.Serialization;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Middleware;
using FIBRADIS.Api.Models;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

builder.Services.AddSingleton<RequestMetricsCollector>();
builder.Services.AddHealthChecks()
       .AddCheck("self", () => HealthCheckResult.Healthy());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}

app.UseMiddleware<RequestTrackingMiddleware>();

app.MapGet("/v1/ping", (HttpContext context) =>
{
    var requestId = context.Features.Get<RequestTrackingFeature>()?.RequestId
                    ?? context.Response.Headers[RequestTrackingMiddleware.RequestIdHeader].ToString()
                    ?? context.TraceIdentifier
                    ?? Guid.NewGuid().ToString("N");

    return Results.Json(new PingResponse("pong", requestId));
});

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
