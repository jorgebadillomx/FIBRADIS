namespace FIBRADIS.Application.Models;

public sealed record DocumentContent(
    Guid DocumentId,
    string Hash,
    DateTimeOffset PublishedAt,
    DateTimeOffset DocumentDate,
    byte[] Content,
    bool IsImageBased);
