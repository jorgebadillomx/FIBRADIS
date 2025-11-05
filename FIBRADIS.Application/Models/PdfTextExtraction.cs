using System.Collections.Generic;

namespace FIBRADIS.Application.Models;

public sealed record PdfTextExtraction(
    string? Text,
    IReadOnlyList<IReadOnlyList<string>> Tables,
    bool IsImageBased,
    double? Confidence);
