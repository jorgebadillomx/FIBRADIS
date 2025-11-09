namespace FIBRADIS.Application.Models.Documents;

public sealed record DownloadResult
{
    public bool Success { get; init; }
    public bool NotModified { get; init; }
    public bool Ignored { get; init; }
    public string? FailureReason { get; init; }
    public DocumentBinary? Binary { get; init; }
    public DocumentRecord? Document { get; init; }
}
