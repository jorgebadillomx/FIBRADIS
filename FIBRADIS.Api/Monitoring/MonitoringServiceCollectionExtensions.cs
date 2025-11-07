using System;
using System.IO;
using System.Reflection;
using FIBRADIS.Api.Monitoring.HealthChecks;
using FIBRADIS.Infrastructure.Observability;
using FIBRADIS.Infrastructure.Observability.Health;
using FIBRADIS.Infrastructure.Observability.Jobs;
using FIBRADIS.Infrastructure.Observability.Logging;
using FIBRADIS.Infrastructure.Observability.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace FIBRADIS.Api.Monitoring;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddFibradisObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection("Monitoring"))
            .ValidateDataAnnotations();

        services.PostConfigure<MonitoringOptions>(options =>
        {
            options.Api ??= new MonitoringOptions.ApiOptions();
            options.Authentication ??= new MonitoringOptions.AuthenticationOptions();

            if (string.IsNullOrWhiteSpace(options.Api.PublicPathPrefix))
            {
                options.Api.PublicPathPrefix = "/v1/";
            }

            if (options.Api.PublicLatencyThresholdMs <= 0)
            {
                options.Api.PublicLatencyThresholdMs = 800;
            }

            options.Authentication.ProbeTokens ??= Array.Empty<string>();
            if (options.Authentication.ProbeTokens.Length == 0)
            {
                options.Authentication.ProbeTokens = new[] { "sub:demo;role:admin" };
            }
        });

        services.TryAddSingleton<ISystemUptimeProvider, SystemUptimeService>();
        services.TryAddSingleton<IJobContextAccessor, JobContextAccessor>();
        services.TryAddSingleton<ObservabilityMetricsRegistry>();

        services.TryAddSingleton<ISqlServerHealthProbe, NoopSqlServerHealthProbe>();
        services.TryAddSingleton<IBackgroundJobHealthProbe, NoopBackgroundJobHealthProbe>();
        services.TryAddSingleton<IStorageHealthProbe, NoopStorageHealthProbe>();
        services.TryAddSingleton<IApiTokenHealthProbe, DefaultApiTokenHealthProbe>();

        services.AddSingleton<ApiLatencyHealthCheck>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MonitoringOptions>>().Value;
            return new ApiLatencyHealthCheck(
                sp.GetRequiredService<RequestMetricsCollector>(),
                options.Api.PublicPathPrefix,
                TimeSpan.FromMilliseconds(Math.Max(options.Api.PublicLatencyThresholdMs, 1)));
        });

        services.AddSingleton<CorrelationLogEnricher>();

        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sqlserver", failureStatus: HealthStatus.Unhealthy, tags: new[] { "db", "critical" })
            .AddCheck<HangfireHealthCheck>("hangfire", failureStatus: HealthStatus.Unhealthy, tags: new[] { "jobs", "critical" })
            .AddCheck<DocumentStorageHealthCheck>("storage_documents", failureStatus: HealthStatus.Degraded, tags: new[] { "storage", "warning" })
            .AddCheck<ApiLatencyHealthCheck>("api_public", failureStatus: HealthStatus.Degraded, tags: new[] { "http", "info" })
            .AddCheck<ApiTokenHealthCheck>("api_private", failureStatus: HealthStatus.Degraded, tags: new[] { "auth", "info" });

        var otlpSection = configuration.GetSection("Monitoring:Otlp");
        var otlpEnabled = otlpSection.GetValue("Enabled", false);
        var otlpEndpoint = otlpSection.GetValue<string?>("Endpoint");

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: "FIBRADIS.Api",
                    serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                    serviceInstanceId: Environment.MachineName);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddMeter(ObservabilityMetricsRegistry.MeterName);
                metrics.AddPrometheusExporter(options =>
                {
                    options.ScrapeEndpointPath = "/metrics";
                });
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    });
                if (otlpEnabled)
                {
                    tracing.AddOtlpExporter(exporterOptions =>
                    {
                        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                        {
                            exporterOptions.Endpoint = new Uri(otlpEndpoint);
                        }

                        exporterOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
                }
            });

        return services;
    }

    public static WebApplicationBuilder UseFibradisSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var environment = context.HostingEnvironment;
            var enricher = services.GetRequiredService<CorrelationLogEnricher>();

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.With(enricher)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Information()
                .WriteTo.Console(new JsonFormatter(renderMessage: true));

            if (!environment.IsDevelopment())
            {
                var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                configuration.WriteTo.File(
                    new JsonFormatter(renderMessage: true),
                    Path.Combine(logDirectory, "observability.log"),
                    rollingInterval: RollingInterval.Day,
                    shared: true);
            }
        });

        return builder;
    }
}
