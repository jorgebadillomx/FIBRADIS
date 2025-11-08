namespace FIBRADIS.Application.Models;

public sealed record NewsIngestionResult(
    IReadOnlyList<NewsRecord> PendingNews,
    int Downloaded,
    int Duplicates,
    int TokensUsed,
    decimal CostUsd);
