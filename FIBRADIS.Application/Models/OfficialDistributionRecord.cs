using System;

namespace FIBRADIS.Application.Models;

public sealed record OfficialDistributionRecord
{
    public string Ticker { get; init; } = string.Empty;

    public DateTime PayDate { get; init; }

    public DateTime? ExDate { get; init; }

    public decimal GrossPerCbfi { get; init; }

    public string Currency { get; init; } = "MXN";

    public string Type { get; init; } = "Dividend";

    public string Source { get; init; } = "Official";

    public string? PeriodTag { get; init; }
}
