namespace FIBRADIS.Application.Models;

public sealed record PositionSnapshotDto
{
    public string Ticker { get; init; } = default!;

    public decimal Qty { get; init; }

    public decimal AvgCost { get; init; }

    public decimal Invested { get; init; }

    public decimal? MarketPrice { get; init; }

    public decimal Value { get; init; }

    public decimal Pnl { get; init; }

    public decimal Weight { get; init; }

    public decimal? YieldTtm { get; init; }

    public decimal? YieldForward { get; init; }
}
