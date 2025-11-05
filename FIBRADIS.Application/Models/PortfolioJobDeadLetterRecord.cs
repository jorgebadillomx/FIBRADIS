using System;

namespace FIBRADIS.Application.Models;

public sealed record PortfolioJobDeadLetterRecord
{
    public required Guid JobRunId { get; init; }

    public required string UserId { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset FailedAt { get; init; }

    public required string ExceptionType { get; init; }

    public required string Message { get; init; }

    public string? StackTrace { get; init; }
}
