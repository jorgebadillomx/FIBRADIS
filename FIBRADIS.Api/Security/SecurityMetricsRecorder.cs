using System;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Infrastructure.Observability.Metrics;

namespace FIBRADIS.Api.Security;

public sealed class SecurityMetricsRecorder : ISecurityMetricsRecorder
{
    private readonly ObservabilityMetricsRegistry _metrics;

    public SecurityMetricsRecorder(ObservabilityMetricsRegistry metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public void RecordAuthLogin() => _metrics.AuthLoginsTotal.Add(1);

    public void RecordAuthRefresh() => _metrics.AuthRefreshTotal.Add(1);

    public void RecordAuthFailed() => _metrics.AuthFailedTotal.Add(1);

    public void RecordRateLimitBlocked() => _metrics.RateLimitBlockedTotal.Add(1);

    public void RecordByokKeyActive() => _metrics.ByokKeysActiveTotal.Add(1);

    public void RecordByokUsage(int tokens)
    {
        _metrics.ByokUsageTokensTotal.Add(tokens);
    }
}
