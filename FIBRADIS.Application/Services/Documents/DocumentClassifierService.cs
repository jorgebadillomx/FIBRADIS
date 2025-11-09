using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Application.Services.Documents;

public sealed class DocumentClassifierService : IDocumentClassifier
{
    private static readonly Regex QuarterRegex = new("(?<quarter>[1-4])\\s*(?:T|Q)\\s*(?<year>(?:20)?\\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YearRegex = new("(?<year>(?:20)?\\d{2})\\s*(?:annual|anual|report|reporte)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TickerRegex = new("\\b(F[IÍ]BRA[A-Z]{0,4}|[A-Z]{3,5})\\b", RegexOptions.Compiled);

    public DocumentClassificationResult Classify(DocumentTextRecord text, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var content = text.Text ?? string.Empty;
        var normalized = content.ToLowerInvariant();
        var kind = DocumentKind.Other;
        decimal confidence = 0.35m;

        if (Contains(normalized, "hecho relevante") || Contains(normalized, "relevant event"))
        {
            kind = DocumentKind.HechoRelevante;
            confidence = 0.85m;
        }
        else if (Contains(normalized, "aviso") && Contains(normalized, "distrib"))
        {
            kind = DocumentKind.DistributionNotice;
            confidence = 0.8m;
        }
        else if (Contains(normalized, "presentaci") || Contains(normalized, "investor presentation"))
        {
            kind = DocumentKind.Presentation;
            confidence = 0.75m;
        }
        else if (Contains(normalized, "trimestre") || Contains(normalized, "quarter") || Contains(normalized, "financial results"))
        {
            kind = DocumentKind.Quarterly;
            confidence = 0.8m;
        }

        var ticker = ResolveTicker(content, metadata);
        var period = ResolvePeriod(content, metadata);

        if (!string.IsNullOrWhiteSpace(ticker))
        {
            confidence = Math.Max(confidence, 0.6m);
        }

        if (!string.IsNullOrWhiteSpace(period) && kind == DocumentKind.Quarterly)
        {
            confidence = Math.Max(confidence, 0.9m);
        }

        return new DocumentClassificationResult(kind, ticker, period, confidence);
    }

    private static string? ResolveTicker(string content, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.TryGetValue("ticker", out var metaTicker) && !string.IsNullOrWhiteSpace(metaTicker))
            {
                return metaTicker.Trim().ToUpperInvariant();
            }
        }

        var match = TickerRegex.Match(content);
        if (match.Success)
        {
            var candidate = match.Value.Trim().ToUpperInvariant();
            if (candidate.Length is >= 3 and <= 6)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolvePeriod(string content, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.TryGetValue("period", out var metaPeriod) && !string.IsNullOrWhiteSpace(metaPeriod))
            {
                return metaPeriod.Trim().ToUpperInvariant();
            }
        }

        var quarterMatch = QuarterRegex.Match(content);
        if (quarterMatch.Success)
        {
            var quarter = quarterMatch.Groups["quarter"].Value;
            var year = NormalizeYear(quarterMatch.Groups["year"].Value);
            if (!string.IsNullOrWhiteSpace(year))
            {
                return $"{year}Q{quarter}";
            }
        }

        var yearMatch = YearRegex.Match(content);
        if (yearMatch.Success)
        {
            var year = NormalizeYear(yearMatch.Groups["year"].Value);
            if (!string.IsNullOrWhiteSpace(year))
            {
                return $"{year}FY";
            }
        }

        return null;
    }

    private static string? NormalizeYear(string yearRaw)
    {
        if (string.IsNullOrWhiteSpace(yearRaw))
        {
            return null;
        }

        if (yearRaw.Length == 2)
        {
            yearRaw = $"20{yearRaw}";
        }

        if (int.TryParse(yearRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            if (year is >= 2000 and <= 2100)
            {
                return year.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool Contains(string content, string value)
    {
        return content.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
