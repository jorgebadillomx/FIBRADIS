namespace FIBRADIS.Application.Interfaces.Admin;

public interface IAdminMetricsRecorder
{
    void SetActiveUsers(int activeUsers);
    void IncrementAuditEntries();
    void IncrementSettingsChanges();
    void RecordRoleChange(string fromRole, string toRole);
    void NotifyMaintenanceModeEnabled();
}
