using System;

namespace FIBRADIS.Application.Models;

public sealed record SecurityDto(
    string Ticker,
    string Name,
    string? Sector,
    decimal? LastPrice,
    DateTimeOffset? LastPriceDate,
    decimal? YieldTtm,
    decimal? YieldForward,
    decimal? NavPerCbfi,
    decimal? Ltv,
    decimal? Occupancy,
    DateTimeOffset? UpdatedAt);
