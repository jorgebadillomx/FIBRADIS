namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentParseRequest(Guid DocumentId, string ParserVersion);
