namespace FIBRADIS.Application.Models;

public sealed record PortfolioMetricsDto
{
    public decimal Invested { get; init; }

    public decimal Value { get; init; }

    public decimal Pnl { get; init; }

    public decimal? YieldTtm { get; init; }

    public decimal? YieldForward { get; init; }
}
