namespace FIBRADIS.Application.Models;

public sealed record NewsClassification(
    string? FibraTicker,
    string? Sector,
    NewsSentiment Sentiment,
    int TokensUsed,
    decimal CostUsd,
    string Provider);
