namespace FIBRADIS.Application.Models;

public sealed record NewsUpdate(
    string? Title,
    string? Summary,
    string? FibraTicker,
    string? Sector,
    NewsSentiment? Sentiment);
