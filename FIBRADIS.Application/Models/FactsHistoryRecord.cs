namespace FIBRADIS.Application.Models;

public sealed record FactsHistoryRecord(
    Guid RecordId,
    Guid DocumentId,
    Guid FactId,
    string FibraTicker,
    string PeriodTag,
    DateTimeOffset RecordedAtUtc,
    decimal? NavPerCbfi,
    decimal? Noi,
    decimal? Affo,
    decimal? Ltv,
    decimal? Occupancy,
    decimal? Dividends,
    int Score,
    string SourceUrl,
    string ParserVersion,
    string Hash);
