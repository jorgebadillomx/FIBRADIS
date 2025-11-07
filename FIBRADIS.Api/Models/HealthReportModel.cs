using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Models;

public sealed record HealthReportModel(string Status, IReadOnlyList<HealthReportEntryModel> Checks, string Uptime)
{
    public static HealthReportModel FromReport(HealthReport report, TimeSpan uptime)
    {
        var checks = report.Entries
            .Select(pair => new HealthReportEntryModel(
                pair.Key,
                pair.Value.Status.ToString(),
                pair.Value.Description,
                FormatDuration(pair.Value.Duration)))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HealthReportModel(report.Status.ToString(), checks, FormatUptime(uptime));
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalSeconds < 1)
        {
            return "<1s";
        }

        var parts = new List<string>(4);
        if (uptime.Days > 0)
        {
            parts.Add($"{uptime.Days}d");
        }

        if (uptime.Hours > 0)
        {
            parts.Add($"{uptime.Hours}h");
        }

        if (uptime.Minutes > 0)
        {
            parts.Add($"{uptime.Minutes}m");
        }

        parts.Add($"{uptime.Seconds}s");
        return string.Join(' ', parts);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            return "0ms";
        }

        var milliseconds = duration.TotalMilliseconds;
        if (milliseconds < 1000)
        {
            return $"{milliseconds.ToString("0.##", CultureInfo.InvariantCulture)}ms";
        }

        return $"{duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
    }
}

public sealed record HealthReportEntryModel(string Name, string Status, string? Description, string Duration);
