namespace FIBRADIS.Application.Models;

public sealed record ParseFactsRequest
{
    public Guid DocumentId { get; init; }
    public string FibraTicker { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = "1.0";
}
