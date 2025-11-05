namespace FIBRADIS.Application.Models;

public sealed record DocumentFactsRecord(
    Guid FactId,
    Guid DocumentId,
    string FibraTicker,
    string PeriodTag,
    decimal? NavPerCbfi,
    decimal? Noi,
    decimal? Affo,
    decimal? Ltv,
    decimal? Occupancy,
    decimal? Dividends,
    int Score,
    string SourceUrl,
    string ParserVersion,
    string Hash,
    DateTimeOffset ParsedAtUtc,
    bool RequiresReview,
    bool IsSuperseded)
{
    public int FieldsFound => new[] { NavPerCbfi, Noi, Affo, Ltv, Occupancy, Dividends }.Count(value => value.HasValue);
}
