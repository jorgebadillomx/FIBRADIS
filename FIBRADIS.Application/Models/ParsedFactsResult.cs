namespace FIBRADIS.Application.Models;

public sealed record ParsedFactsResult
{
    public string FibraTicker { get; init; } = string.Empty;
    public string PeriodTag { get; init; } = string.Empty;
    public decimal? NavPerCbfi { get; init; }
    public decimal? Noi { get; init; }
    public decimal? Affo { get; init; }
    public decimal? Ltv { get; init; }
    public decimal? Occupancy { get; init; }
    public decimal? Dividends { get; init; }
    public int Score { get; init; }
    public string SourceUrl { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = string.Empty;
}
