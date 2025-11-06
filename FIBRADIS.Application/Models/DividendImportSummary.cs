using System.Collections.Generic;

namespace FIBRADIS.Application.Models;

public sealed record DividendImportSummary
{
    public int CountImported { get; init; }

    public int CountDuplicates { get; init; }

    public int CountFailed { get; init; }

    public IReadOnlyDictionary<string, string> Warnings { get; init; } = new Dictionary<string, string>();
}
