using System;

namespace FIBRADIS.Application.Models;

public sealed record PortfolioRecalcMetricsSnapshot
{
    public required decimal Invested { get; init; }

    public required decimal Value { get; init; }

    public required decimal Pnl { get; init; }

    public decimal? YieldTtm { get; init; }

    public decimal? YieldForward { get; init; }

    public decimal? TimeWeightedReturn { get; init; }

    public decimal? MoneyWeightedReturn { get; init; }

    public decimal? AnnualizedTimeWeightedReturn { get; init; }

    public decimal? AnnualizedMoneyWeightedReturn { get; init; }

    public required DateTimeOffset CalculatedAt { get; init; }
}
