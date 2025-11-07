using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading;

namespace FIBRADIS.Infrastructure.Observability.Metrics;

public sealed class ObservabilityMetricsRegistry : IDisposable
{
    public const string MeterName = "FIBRADIS.Observability";
    private const string Version = "1.0.0";

    private readonly Meter _meter;
    private readonly ISystemUptimeProvider _uptimeProvider;
    private double _dividendsVerifiedRatio;
    private double _factsScoreAverage;

    public ObservabilityMetricsRegistry(ISystemUptimeProvider uptimeProvider)
    {
        _uptimeProvider = uptimeProvider ?? throw new ArgumentNullException(nameof(uptimeProvider));
        _meter = new Meter(MeterName, Version);

        HttpRequestsTotal = _meter.CreateCounter<long>("http_requests_total", unit: "requests", description: "Total de solicitudes HTTP procesadas por endpoint.");
        HttpRequestDurationSeconds = _meter.CreateHistogram<double>("http_request_duration_seconds", unit: "s", description: "Duración de las solicitudes HTTP en segundos.");
        JobsTotal = _meter.CreateCounter<long>("jobs_total", unit: "jobs", description: "Total de jobs ejecutados por cola.");
        JobsFailuresTotal = _meter.CreateCounter<long>("jobs_failures_total", unit: "jobs", description: "Total de jobs fallidos por cola.");
        JobsDurationSeconds = _meter.CreateHistogram<double>("jobs_duration_seconds", unit: "s", description: "Duración de jobs de Hangfire en segundos.");
        DbQueryDurationSeconds = _meter.CreateHistogram<double>("db_query_duration_seconds", unit: "s", description: "Duración de consultas SQL en segundos.");
        ApiCacheHitsTotal = _meter.CreateCounter<long>("api_cache_hits_total", unit: "hits", description: "Número de aciertos de caché en API pública.");
        ApiCacheMissTotal = _meter.CreateCounter<long>("api_cache_miss_total", unit: "miss", description: "Número de misses de caché en API pública.");
        PortfolioReplacementsTotal = _meter.CreateCounter<long>("portfolio_replacements_total", unit: "operations", description: "Número de cargas de portafolio procesadas.");
        AuthLoginsTotal = _meter.CreateCounter<long>("auth_logins_total", unit: "events", description: "Total de autenticaciones exitosas.");
        AuthRefreshTotal = _meter.CreateCounter<long>("auth_refresh_total", unit: "events", description: "Total de refresh tokens emitidos.");
        AuthFailedTotal = _meter.CreateCounter<long>("auth_failed_total", unit: "events", description: "Total de autenticaciones fallidas.");
        RateLimitBlockedTotal = _meter.CreateCounter<long>("rate_limit_blocked_total", unit: "requests", description: "Solicitudes bloqueadas por rate limiting.");
        ByokKeysActiveTotal = _meter.CreateCounter<long>("byok_keys_active_total", unit: "keys", description: "Claves BYO registradas activas.");
        ByokUsageTokensTotal = _meter.CreateCounter<long>("byok_usage_tokens_total", unit: "tokens", description: "Tokens consumidos mediante BYO Key.");

        _meter.CreateObservableGauge(
            "dividends_verified_ratio",
            () => new[] { new Measurement<double>(Volatile.Read(ref _dividendsVerifiedRatio)) },
            "ratio",
            "Porcentaje de eventos de dividendos verificados.");
        _meter.CreateObservableGauge(
            "facts_score_avg",
            () => new[] { new Measurement<double>(Volatile.Read(ref _factsScoreAverage)) },
            "score",
            "Promedio de score en el parsing de facts PDF.");
        _meter.CreateObservableGauge(
            "system_uptime_seconds",
            () => new[] { new Measurement<double>(_uptimeProvider.GetUptime().TotalSeconds) },
            "s",
            "Tiempo activo del servidor en segundos.");

        SetDividendsVerifiedRatio(1d);
    }

    public Counter<long> HttpRequestsTotal { get; }
    public Histogram<double> HttpRequestDurationSeconds { get; }
    public Counter<long> JobsTotal { get; }
    public Counter<long> JobsFailuresTotal { get; }
    public Histogram<double> JobsDurationSeconds { get; }
    public Histogram<double> DbQueryDurationSeconds { get; }
    public Counter<long> ApiCacheHitsTotal { get; }
    public Counter<long> ApiCacheMissTotal { get; }
    public Counter<long> PortfolioReplacementsTotal { get; }
    public Counter<long> AuthLoginsTotal { get; }
    public Counter<long> AuthRefreshTotal { get; }
    public Counter<long> AuthFailedTotal { get; }
    public Counter<long> RateLimitBlockedTotal { get; }
    public Counter<long> ByokKeysActiveTotal { get; }
    public Counter<long> ByokUsageTokensTotal { get; }

    public void SetDividendsVerifiedRatio(double ratio)
    {
        var normalized = Math.Clamp(ratio, 0d, 1d);
        Volatile.Write(ref _dividendsVerifiedRatio, normalized);
    }

    public void SetFactsScoreAverage(double value)
    {
        Volatile.Write(ref _factsScoreAverage, value);
    }

    public void RecordJobDuration(string queue, TimeSpan duration)
    {
        var queueTag = queue ?? "default";
        JobsDurationSeconds.Record(duration.TotalSeconds, KeyValuePair.Create<string, object?>("queue", queueTag));
    }

    public void RecordJobResult(string queue, bool success)
    {
        var queueTag = queue ?? "default";
        JobsTotal.Add(1, KeyValuePair.Create<string, object?>("queue", queueTag));
        if (!success)
        {
            JobsFailuresTotal.Add(1, KeyValuePair.Create<string, object?>("queue", queueTag));
        }
    }

    public void RecordHttpRequest(string method, string path, int statusCode, TimeSpan duration)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "unknown" : path;
        HttpRequestsTotal.Add(1,
            KeyValuePair.Create<string, object?>("method", method ?? "UNKNOWN"),
            KeyValuePair.Create<string, object?>("path", normalizedPath),
            KeyValuePair.Create<string, object?>("status_code", statusCode.ToString(CultureInfo.InvariantCulture)));
        HttpRequestDurationSeconds.Record(duration.TotalSeconds,
            KeyValuePair.Create<string, object?>("method", method ?? "UNKNOWN"),
            KeyValuePair.Create<string, object?>("path", normalizedPath));
    }

    public void RecordDatabaseQuery(string name, TimeSpan duration)
    {
        var queryName = string.IsNullOrWhiteSpace(name) ? "unknown" : name;
        DbQueryDurationSeconds.Record(duration.TotalSeconds, KeyValuePair.Create<string, object?>("query", queryName));
    }

    public void RecordCacheHit()
    {
        ApiCacheHitsTotal.Add(1);
    }

    public void RecordCacheMiss()
    {
        ApiCacheMissTotal.Add(1);
    }

    public void RecordPortfolioReplacement()
    {
        PortfolioReplacementsTotal.Add(1);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
