namespace FIBRADIS.Application.Models.Admin;

public sealed record SystemSettings
{
    public string MarketHoursMx { get; init; } = string.Empty;
    public int LlmMaxTokensPerUser { get; init; }
        = 0;
    public int SecurityMaxUploadSize { get; init; }
        = 0;
    public bool SystemMaintenanceMode { get; init; }
        = false;
}
