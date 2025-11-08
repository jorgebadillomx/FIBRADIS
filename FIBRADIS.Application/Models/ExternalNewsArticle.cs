namespace FIBRADIS.Application.Models;

public sealed record ExternalNewsArticle(
    string Title,
    string Summary,
    string Url,
    DateTimeOffset PublishedAt,
    string Source);
