namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentFactsRequest(Guid DocumentId, string ParserVersion);
