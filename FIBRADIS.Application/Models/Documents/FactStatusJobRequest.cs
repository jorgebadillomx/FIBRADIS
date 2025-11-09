using System;

namespace FIBRADIS.Application.Models.Documents;

public sealed record FactStatusJobRequest
{
    public Guid DocumentId { get; init; }
    public string Stage { get; init; } = "facts";
    public string Status { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Details { get; init; }
    public string? ParserVersion { get; init; }
    public string? Hash { get; init; }
    public DocumentStatus? DocumentStatus { get; init; }
}
