using System;

namespace FIBRADIS.Application.Models;

public sealed class DistributionRecord
{
    public Guid Id { get; set; }

    public string Ticker { get; set; } = string.Empty;

    public DateTime? ExDate { get; set; }

    public DateTime PayDate { get; set; }

    public decimal GrossPerCbfi { get; set; }

    public string Currency { get; set; } = "MXN";

    public string? PeriodTag { get; set; }

    public string Source { get; set; } = string.Empty;

    public decimal Confidence { get; set; }

    public string Type { get; set; } = "Dividend";

    public string Status { get; set; } = "imported";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DistributionRecord Clone()
    {
        return new DistributionRecord
        {
            Id = Id,
            Ticker = Ticker,
            ExDate = ExDate,
            PayDate = PayDate,
            GrossPerCbfi = GrossPerCbfi,
            Currency = Currency,
            PeriodTag = PeriodTag,
            Source = Source,
            Confidence = Confidence,
            Type = Type,
            Status = Status,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
