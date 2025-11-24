using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class PdfFactsParserService : IPdfFactsParserService
{
    private const int MinimumScore = 70;
    private const int DecimalPrecision = 6;
    private const decimal MillionThreshold = 100_000m;
    private static readonly Regex PeriodRegex = new("(?<quarter>[1-4])\\s*(?:T|Q)\\s*(?<year>(?:20)?\\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FieldRegex = new("(?<label>NAV/CBFI|NAV|NOI|AFFO|FFO|LTV|Ocupaci[óo]n|Occupancy|Dividendo[s]?)(?:[^-+\\d]*)(?<value>[-+]?\\d[\\d.,]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDocumentStorage _documentStorage;
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IOcrProvider _ocrProvider;
    private readonly IFactsRepository _factsRepository;
    private readonly IFactsMetricsCollector _metricsCollector;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ILogger<PdfFactsParserService> _logger;

    public PdfFactsParserService(
        IDocumentStorage documentStorage,
        IPdfTextExtractor pdfTextExtractor,
        IOcrProvider ocrProvider,
        IFactsRepository factsRepository,
        IFactsMetricsCollector metricsCollector,
        IClock clock,
        ICorrelationIdAccessor correlationIdAccessor,
        ILogger<PdfFactsParserService> logger)
    {
        _documentStorage = documentStorage ?? throw new ArgumentNullException(nameof(documentStorage));
        _pdfTextExtractor = pdfTextExtractor ?? throw new ArgumentNullException(nameof(pdfTextExtractor));
        _ocrProvider = ocrProvider ?? throw new ArgumentNullException(nameof(ocrProvider));
        _factsRepository = factsRepository ?? throw new ArgumentNullException(nameof(factsRepository));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ParsedFactsResult> ParseAsync(ParseFactsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required.", nameof(request));
        }

        _metricsCollector.RecordInvocation();
        var stopwatch = Stopwatch.StartNew();
        var requestId = ResolveRequestId();

        try
        {
            ct.ThrowIfCancellationRequested();

            var existing = await _factsRepository.GetDocumentFactsAsync(request.DocumentId, request.ParserVersion, request.Hash, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                stopwatch.Stop();
                _metricsCollector.RecordSuccess(stopwatch.Elapsed, existing.FieldsFound, existing.Score);

                _logger.LogInformation(
                    "pdf.facts.parse idempotent for {DocumentId} (ticker={Ticker}, requestId={RequestId})",
                    request.DocumentId,
                    request.FibraTicker,
                    requestId);

                return MapToResult(existing);
            }

            var document = await _documentStorage.GetDocumentAsync(request.DocumentId, ct).ConfigureAwait(false);
            if (!string.Equals(document.Hash, request.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"El hash del documento {request.DocumentId} no coincide con la petición.");
            }

            ct.ThrowIfCancellationRequested();

            var extraction = document.IsImageBased
                ? new PdfTextExtraction(null, Array.Empty<IReadOnlyList<string>>(), true, null)
                : await _pdfTextExtractor.ExtractAsync(document.Content, ct).ConfigureAwait(false);

            string? text = extraction.Text;
            var tables = extraction.Tables ?? Array.Empty<IReadOnlyList<string>>();
            var confidence = extraction.Confidence ?? 1d;
            var usedOcr = false;

            if (document.IsImageBased || string.IsNullOrWhiteSpace(text))
            {
                var ocr = await _ocrProvider.ExtractAsync(document.Content, ct).ConfigureAwait(false);
                text = ocr.Text;
                tables = ocr.Tables ?? Array.Empty<IReadOnlyList<string>>();
                confidence = ocr.Confidence;
                usedOcr = true;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"El documento {request.DocumentId} no contiene texto interpretable.");
            }

            ct.ThrowIfCancellationRequested();

            var periodTag = DetectPeriod(text, document.DocumentDate);
            var extracted = ExtractMetrics(text, tables);

            decimal? nav = extracted.Nav is null ? null : Round(extracted.Nav.Value);
            var noi = NormalizeMillions(extracted.Noi);
            var affo = NormalizeMillions(extracted.Affo);
            var ltv = NormalizePercentage(extracted.Ltv);
            var occupancy = NormalizePercentage(extracted.Occupancy);
            decimal? dividends = extracted.Dividends is null ? null : Round(extracted.Dividends.Value);

            var score = CalculateScore(extracted.FoundCount, confidence);
            var parsedAt = _clock.UtcNow;
            var factId = Guid.NewGuid();

            var result = new ParsedFactsResult
            {
                FibraTicker = request.FibraTicker,
                PeriodTag = periodTag,
                NavPerCbfi = nav,
                Noi = noi,
                Affo = affo,
                Ltv = ltv,
                Occupancy = occupancy,
                Dividends = dividends,
                Score = score,
                SourceUrl = request.Url,
                ParserVersion = request.ParserVersion
            };

            var record = new DocumentFactsRecord(
                factId,
                request.DocumentId,
                request.FibraTicker,
                periodTag,
                nav,
                noi,
                affo,
                ltv,
                occupancy,
                dividends,
                score,
                request.Url,
                request.ParserVersion,
                request.Hash,
                parsedAt,
                score < MinimumScore,
                false);

            if (record.RequiresReview)
            {
                await _factsRepository.SavePendingReviewAsync(record, ct).ConfigureAwait(false);
            }
            else
            {
                await _factsRepository.MarkSupersededAsync(request.FibraTicker, periodTag, ct).ConfigureAwait(false);
                await _factsRepository.SaveDocumentFactsAsync(record, ct).ConfigureAwait(false);
            }

            var historyRecord = new FactsHistoryRecord(
                Guid.NewGuid(),
                request.DocumentId,
                factId,
                request.FibraTicker,
                periodTag,
                parsedAt,
                nav,
                noi,
                affo,
                ltv,
                occupancy,
                dividends,
                score,
                request.Url,
                request.ParserVersion,
                request.Hash);

            await _factsRepository.AppendHistoryAsync(historyRecord, ct).ConfigureAwait(false);

            stopwatch.Stop();
            _metricsCollector.RecordSuccess(stopwatch.Elapsed, extracted.FoundCount, score);

            _logger.LogInformation(
                "pdf.facts.parse completed for {Ticker} period {Period} (requestId={RequestId}, score={Score}, fields={Fields}, ocr={Ocr})",
                request.FibraTicker,
                periodTag,
                requestId,
                score,
                extracted.FoundCount,
                usedOcr);

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed);
            _logger.LogWarning(
                "pdf.facts.parse cancelled for {DocumentId} (ticker={Ticker}, requestId={RequestId})",
                request.DocumentId,
                request.FibraTicker,
                requestId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed);
            _logger.LogError(
                ex,
                "pdf.facts.parse failed for {DocumentId} (ticker={Ticker}, requestId={RequestId})",
                request.DocumentId,
                request.FibraTicker,
                requestId);
            throw;
        }
    }

    private static ParsedFactsResult MapToResult(DocumentFactsRecord record)
    {
        return new ParsedFactsResult
        {
            FibraTicker = record.FibraTicker,
            PeriodTag = record.PeriodTag,
            NavPerCbfi = record.NavPerCbfi,
            Noi = record.Noi,
            Affo = record.Affo,
            Ltv = record.Ltv,
            Occupancy = record.Occupancy,
            Dividends = record.Dividends,
            Score = record.Score,
            SourceUrl = record.SourceUrl,
            ParserVersion = record.ParserVersion
        };
    }

    private static string DetectPeriod(string text, DateTimeOffset documentDate)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var match = PeriodRegex.Match(text);
            if (match.Success)
            {
                var quarter = match.Groups["quarter"].Value;
                var yearValue = match.Groups["year"].Value;
                if (yearValue.Length == 2)
                {
                    yearValue = $"20{yearValue}";
                }

                return $"{quarter}T{yearValue}";
            }
        }

        var derivedQuarter = ((documentDate.Month - 1) / 3) + 1;
        return $"{derivedQuarter}T{documentDate.Year}";
    }

    private static ExtractedMetrics ExtractMetrics(string text, IReadOnlyList<IReadOnlyList<string>> tables)
    {
        var nav = ExtractDecimal(tables, text, "NAV/CBFI", "NAV");
        var noi = ExtractDecimal(tables, text, "NOI");
        var affo = ExtractDecimal(tables, text, "AFFO", "FFO");
        var ltv = ExtractDecimal(tables, text, "LTV");
        var occupancy = ExtractDecimal(tables, text, "Ocupación", "Ocupacion", "Occupancy");
        var dividends = ExtractDecimal(tables, text, "Dividendo", "Dividends");

        return new ExtractedMetrics(nav, noi, affo, ltv, occupancy, dividends);
    }

    private static decimal? ExtractDecimal(IReadOnlyList<IReadOnlyList<string>> tables, string text, params string[] keywords)
    {
        var fromTable = ExtractFromTables(tables, keywords);
        if (fromTable.HasValue)
        {
            return fromTable;
        }

        return ExtractFromText(text, keywords);
    }

    private static decimal? ExtractFromTables(IReadOnlyList<IReadOnlyList<string>> tables, string[] keywords)
    {
        foreach (var row in tables)
        {
            for (var index = 0; index < row.Count; index++)
            {
                var cell = row[index];
                if (string.IsNullOrWhiteSpace(cell))
                {
                    continue;
                }

                if (!keywords.Any(keyword => cell.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                for (var valueIndex = index + 1; valueIndex < row.Count; valueIndex++)
                {
                    if (TryParseDecimal(row[valueIndex], out var value))
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static decimal? ExtractFromText(string text, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in FieldRegex.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["label"].Value;
            if (!keywords.Any(keyword => label.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var candidate = match.Groups["value"].Value;
            if (TryParseDecimal(candidate, out var value))
            {
                return value;
            }
        }

        foreach (var keyword in keywords)
        {
            var pattern = $"{Regex.Escape(keyword)}[^-+\\d]*([-+]?\\d[\\d.,]*)";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var match = regex.Match(text);
            if (match.Success && TryParseDecimal(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseDecimal(string? input, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var sanitized = SanitizeNumeric(input);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        return decimal.TryParse(sanitized, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);
    }

    private static string SanitizeNumeric(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsDigit(ch) || ch is '-' or '+' || ch is '.' or ',')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                continue;
            }
        }

        var sanitized = builder.ToString();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        var commaCount = sanitized.Count(c => c == ',');
        var dotCount = sanitized.Count(c => c == '.');

        if (commaCount > 1 && dotCount == 0)
        {
            sanitized = sanitized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (commaCount == 1 && dotCount == 0)
        {
            sanitized = sanitized.Replace(',', '.');
        }
        else if (commaCount >= 1 && dotCount >= 1)
        {
            sanitized = sanitized.Replace(",", string.Empty);
        }

        return sanitized;
    }

    private static decimal? NormalizeMillions(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Value;
        if (Math.Abs(normalized) > MillionThreshold)
        {
            normalized /= 1_000_000m;
        }

        return Round(normalized);
    }

    private static decimal? NormalizePercentage(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Value;
        if (normalized > 1m)
        {
            normalized /= 100m;
        }

        return Round(normalized);
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, DecimalPrecision, MidpointRounding.AwayFromZero);
    }

    private static int CalculateScore(int fieldsFound, double confidence)
    {
        var coverageScore = Math.Clamp(fieldsFound / 6d, 0d, 1d) * 80d;
        var confidenceScore = Math.Clamp(confidence, 0d, 1d) * 20d;
        var total = coverageScore + confidenceScore;
        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    private string ResolveRequestId()
    {
        return _correlationIdAccessor.CorrelationId ?? Guid.NewGuid().ToString("N");
    }

    private readonly record struct ExtractedMetrics(
        decimal? Nav,
        decimal? Noi,
        decimal? Affo,
        decimal? Ltv,
        decimal? Occupancy,
        decimal? Dividends)
    {
        public int FoundCount => new[] { Nav, Noi, Affo, Ltv, Occupancy, Dividends }.Count(value => value.HasValue);
    }
}
