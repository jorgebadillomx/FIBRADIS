namespace FIBRADIS.Application.Models;

public sealed record SummarizeResult(
    SummaryRecord PublicSummary,
    SummaryRecord PrivateSummary,
    int TotalTokens,
    decimal TotalCost,
    bool TriggerNewsWorkflow);
