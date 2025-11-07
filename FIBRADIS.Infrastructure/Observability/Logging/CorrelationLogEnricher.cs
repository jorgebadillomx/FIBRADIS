using System;
using System.Security.Claims;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Infrastructure.Observability.Jobs;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace FIBRADIS.Infrastructure.Observability.Logging;

public sealed class CorrelationLogEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly IJobContextAccessor _jobContextAccessor;

    public CorrelationLogEnricher(
        IHttpContextAccessor httpContextAccessor,
        ICorrelationIdAccessor correlationIdAccessor,
        IJobContextAccessor jobContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _correlationIdAccessor = correlationIdAccessor;
        _jobContextAccessor = jobContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent is null)
        {
            return;
        }

        var requestId = _correlationIdAccessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            logEvent.RemovePropertyIfPresent("requestId");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("requestId", requestId));
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            logEvent.RemovePropertyIfPresent("userId");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("userId", userId));
        }

        var jobContext = _jobContextAccessor.Current;
        if (jobContext is not null)
        {
            logEvent.RemovePropertyIfPresent("jobRunId");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("jobRunId", jobContext.JobRunId));
            logEvent.RemovePropertyIfPresent("queue");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("queue", jobContext.Queue));
            if (!string.IsNullOrWhiteSpace(jobContext.CorrelationId))
            {
                logEvent.RemovePropertyIfPresent("correlationId");
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("correlationId", jobContext.CorrelationId));
            }
        }
    }
}
