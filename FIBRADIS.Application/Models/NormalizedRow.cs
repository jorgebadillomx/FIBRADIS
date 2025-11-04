namespace FIBRADIS.Application.Models;

public sealed record NormalizedRow
{
    public string Ticker { get; init; } = default!;
    public decimal Qty { get; init; }
    public decimal AvgCost { get; init; }
}
