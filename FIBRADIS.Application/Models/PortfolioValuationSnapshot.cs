using System;

namespace FIBRADIS.Application.Models;

public sealed record PortfolioValuationSnapshot
{
    public required DateTimeOffset AsOf { get; init; }

    public required decimal Value { get; init; }
}
