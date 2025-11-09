namespace FIBRADIS.Application.Models.Documents;

public sealed record DocumentBinary
{
    public Guid DocumentId { get; init; }
    public string Hash { get; init; } = string.Empty;
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/pdf";
    public long ContentLength { get; init; }
    public bool IsImageBased { get; init; }
    public DateTimeOffset DownloadedAt { get; init; }
}
