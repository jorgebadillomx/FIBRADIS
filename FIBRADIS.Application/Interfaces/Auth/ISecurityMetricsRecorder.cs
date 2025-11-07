namespace FIBRADIS.Application.Interfaces.Auth;

public interface ISecurityMetricsRecorder
{
    void RecordAuthLogin();
    void RecordAuthRefresh();
    void RecordAuthFailed();
    void RecordRateLimitBlocked();
    void RecordByokKeyActive();
    void RecordByokUsage(int tokens);
}
