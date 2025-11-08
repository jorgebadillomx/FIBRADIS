namespace FIBRADIS.Application.Models.Admin;

public sealed record SystemSettingsUpdate(string? MarketHoursMx, int? LlmMaxTokensPerUser, int? SecurityMaxUploadSize, bool? SystemMaintenanceMode);
