using FIBRADIS.Application.Models;

namespace FIBRADIS.Api.Models;

public sealed record NewsItemResponse(
    string Title,
    string Summary,
    string Url,
    string Source,
    DateTimeOffset PublishedAt,
    string? Ticker,
    string? Sector,
    string Sentiment)
{
    public static NewsItemResponse FromModel(NewsRecord record)
        => new(
            record.Title,
            record.Summary,
            record.Url,
            record.Source,
            record.PublishedAt,
            record.FibraTicker,
            record.Sector,
            record.Sentiment.ToString().ToLowerInvariant());
}
