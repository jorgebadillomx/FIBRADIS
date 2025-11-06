using System;

namespace FIBRADIS.Application.Services.Models;

public sealed record YahooDividendEvent
{
    public DateTime PayDate { get; init; }

    public DateTime? ExDate { get; init; }

    public decimal GrossAmount { get; init; }

    public string Currency { get; init; } = "MXN";
}
