namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentDownloadRequest(Guid DocumentId, string Url);
