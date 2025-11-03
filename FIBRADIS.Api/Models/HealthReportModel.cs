using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Models;

public sealed record HealthReportModel(string Status, IReadOnlyDictionary<string, HealthReportEntryModel> Details)
{
    public static HealthReportModel FromReport(HealthReport report)
    {
        var details = report.Entries.ToDictionary(
            pair => pair.Key,
            pair => new HealthReportEntryModel(pair.Value.Status.ToString(), pair.Value.Description),
            StringComparer.OrdinalIgnoreCase);

        return new HealthReportModel(report.Status.ToString(), details);
    }
}

public sealed record HealthReportEntryModel(string Status, string? Description);
