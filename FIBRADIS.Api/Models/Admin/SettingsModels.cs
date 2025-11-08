using System.ComponentModel.DataAnnotations;
using FIBRADIS.Application.Models.Admin;

namespace FIBRADIS.Api.Models.Admin;

public sealed record SettingsResponse(
    string MarketHoursMx,
    int LlmMaxTokensPerUser,
    int SecurityMaxUploadSize,
    bool SystemMaintenanceMode)
{
    public static SettingsResponse FromModel(SystemSettings settings)
    {
        return new SettingsResponse(
            settings.MarketHoursMx,
            settings.LlmMaxTokensPerUser,
            settings.SecurityMaxUploadSize,
            settings.SystemMaintenanceMode);
    }
}

public sealed record UpdateSettingsRequest
{
    public string? MarketHoursMx { get; init; }

    [Range(1, int.MaxValue)]
    public int? LlmMaxTokensPerUser { get; init; }

    [Range(1, int.MaxValue)]
    public int? SecurityMaxUploadSize { get; init; }

    public bool? SystemMaintenanceMode { get; init; }
}
