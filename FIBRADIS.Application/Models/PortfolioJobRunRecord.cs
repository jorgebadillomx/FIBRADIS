using System;

namespace FIBRADIS.Application.Models;

public sealed record PortfolioJobRunRecord
{
    public required Guid JobRunId { get; init; }

    public required string UserId { get; init; }

    public required string Reason { get; init; }

    public required DateOnly ExecutionDate { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required string Status { get; init; }

    public int PositionsProcessed { get; init; }

    public bool MetricsUpdated { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? ErrorMessage { get; init; }
}
