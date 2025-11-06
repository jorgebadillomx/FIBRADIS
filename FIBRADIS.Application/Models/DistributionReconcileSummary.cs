namespace FIBRADIS.Application.Models;

public sealed record DistributionReconcileSummary
{
    public int ImportedCount { get; init; }

    public int VerifiedCount { get; init; }

    public int IgnoredCount { get; init; }

    public int SplitCount { get; init; }
}
