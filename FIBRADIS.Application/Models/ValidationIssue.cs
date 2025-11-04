namespace FIBRADIS.Application.Models;

public sealed record ValidationIssue
{
    public int RowNumber { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = "Error";
}
