using System;

namespace FIBRADIS.Application.Models;

public sealed class SecurityEntity
{
    public string Ticker { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Sector { get; set; }
        = null;

    public decimal? LastPrice { get; set; }
        = null;

    public DateTimeOffset? LastPriceDate { get; set; }
        = null;

    public decimal? NavPerCbfi { get; set; }
        = null;

    public decimal? Noi { get; set; }
        = null;

    public decimal? Affo { get; set; }
        = null;

    public decimal? Ltv { get; set; }
        = null;

    public decimal? Occupancy { get; set; }
        = null;

    public decimal? YieldTtm { get; set; }
        = null;

    public decimal? YieldForward { get; set; }
        = null;

    public string? Source { get; set; }
        = null;

    public DateTimeOffset? UpdatedAt { get; set; }
        = null;
}
