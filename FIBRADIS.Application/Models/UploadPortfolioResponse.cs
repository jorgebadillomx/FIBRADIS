namespace FIBRADIS.Application.Models;

public sealed record UploadPortfolioResponse
{
    public int Imported { get; init; }

    public int Ignored { get; init; }

    public int Errors { get; init; }

    public IReadOnlyList<PositionSnapshotDto> Positions { get; init; } = Array.Empty<PositionSnapshotDto>();

    public PortfolioMetricsDto Metrics { get; init; } = new();

    public string RequestId { get; init; } = string.Empty;
}
