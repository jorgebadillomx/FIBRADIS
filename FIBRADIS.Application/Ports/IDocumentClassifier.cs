using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IDocumentClassifier
{
    DocumentClassificationResult Classify(DocumentTextRecord text, IReadOnlyDictionary<string, string>? metadata = null);
}

public sealed record DocumentClassificationResult(
    DocumentKind Kind,
    string? Ticker,
    string? PeriodTag,
    decimal Confidence);
