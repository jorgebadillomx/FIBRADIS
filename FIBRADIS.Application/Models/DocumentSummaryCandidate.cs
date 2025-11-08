namespace FIBRADIS.Application.Models;

public sealed record DocumentSummaryCandidate(
    Guid DocumentId,
    string FibraTicker,
    string PeriodTag,
    string ParserVersion,
    string Hash,
    DocumentFactsRecord? Facts,
    string? DocumentTitle,
    string? DocumentExcerpt,
    string? UploadedBy,
    string? UserId,
    string? ByoKey,
    int RemainingTokenQuota,
    string Provider,
    string? SourceUrl);
