using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryLlmUsageTracker : ILLMUsageTracker
{
    private readonly ConcurrentBag<LLMUsageLog> _logs = new();
    private readonly ISecurityMetricsRecorder _metrics;

    public InMemoryLlmUsageTracker(ISecurityMetricsRecorder metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public Task RecordUsageAsync(string userId, string provider, int tokens, decimal cost, CancellationToken cancellationToken)
    {
        var log = new LLMUsageLog
        {
            UserId = userId,
            Provider = provider,
            TokensUsed = tokens,
            Cost = cost,
            TimestampUtc = DateTime.UtcNow
        };
        _logs.Add(log);
        _metrics.RecordByokUsage(tokens);
        return Task.CompletedTask;
    }

    public Task<int> GetMonthlyUsageAsync(string userId, string provider, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthLogs = _logs.Where(log => log.UserId == userId && log.Provider == provider && log.TimestampUtc.Year == now.Year && log.TimestampUtc.Month == now.Month);
        var total = monthLogs.Sum(log => log.TokensUsed);
        return Task.FromResult(total);
    }
}
