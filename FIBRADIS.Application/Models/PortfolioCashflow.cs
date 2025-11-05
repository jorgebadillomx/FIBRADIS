using System;

namespace FIBRADIS.Application.Models;

public sealed record PortfolioCashflow
{
    public required DateTimeOffset Timestamp { get; init; }

    public required decimal Amount { get; init; }
}
