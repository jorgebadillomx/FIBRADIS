using System.Collections.Generic;

namespace FIBRADIS.Application.Models;

public sealed record OcrExtractionResult(
    string Text,
    IReadOnlyList<IReadOnlyList<string>> Tables,
    double Confidence);
