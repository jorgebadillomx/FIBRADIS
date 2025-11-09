using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services.Documents;

public sealed class DocumentParserService : IDocumentParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorage _documentStorage;
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IOcrProvider _ocrProvider;
    private readonly IDocumentClassifier _classifier;
    private readonly IClock _clock;
    private readonly ILogger<DocumentParserService> _logger;

    public DocumentParserService(
        IDocumentRepository documentRepository,
        IDocumentStorage documentStorage,
        IPdfTextExtractor pdfTextExtractor,
        IOcrProvider ocrProvider,
        IDocumentClassifier classifier,
        IClock clock,
        ILogger<DocumentParserService> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _documentStorage = documentStorage ?? throw new ArgumentNullException(nameof(documentStorage));
        _pdfTextExtractor = pdfTextExtractor ?? throw new ArgumentNullException(nameof(pdfTextExtractor));
        _ocrProvider = ocrProvider ?? throw new ArgumentNullException(nameof(ocrProvider));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ParseResult> ParseAsync(DocumentParseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required", nameof(request));
        }

        var document = await _documentRepository.GetByIdAsync(request.DocumentId, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"Document {request.DocumentId} not found.");

        if (!document.HasHash)
        {
            throw new InvalidOperationException($"Document {request.DocumentId} is missing a binary hash.");
        }

        if (!string.Equals(document.ParserVersion, request.ParserVersion, StringComparison.OrdinalIgnoreCase)
            && document.Status is DocumentStatus.Parsed or DocumentStatus.FactsExtracted)
        {
            _logger.LogInformation(
                "Parser version change detected for {DocumentId}: {CurrentVersion} -> {RequestedVersion}",
                request.DocumentId,
                document.ParserVersion,
                request.ParserVersion);
        }

        var binary = await _documentStorage.GetDocumentAsync(request.DocumentId, ct).ConfigureAwait(false);
        if (!string.Equals(binary.Hash, document.Hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Stored document hash mismatch for {request.DocumentId}.");
        }

        var extraction = await _pdfTextExtractor.ExtractAsync(binary.Content, ct).ConfigureAwait(false);
        var text = extraction.Text;
        var tables = extraction.Tables ?? Array.Empty<IReadOnlyList<string>>();
        var usedOcr = extraction.IsImageBased;
        var confidence = extraction.Confidence ?? 0.5d;

        if (string.IsNullOrWhiteSpace(text) || extraction.IsImageBased)
        {
            var ocr = await _ocrProvider.ExtractAsync(binary.Content, ct).ConfigureAwait(false);
            text = ocr.Text;
            tables = ocr.Tables ?? Array.Empty<IReadOnlyList<string>>();
            confidence = ocr.Confidence;
            usedOcr = true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParseResult
            {
                Success = false,
                RequiresRetry = false,
                FailureReason = "Texto vacío tras OCR"
            };
        }

        var textRecord = new DocumentTextRecord
        {
            DocumentId = request.DocumentId,
            Text = text,
            TablesJson = SerializeTables(tables),
            OcrUsed = usedOcr,
            Pages = tables.Count > 0 ? tables.Count : null,
            ParserVersion = request.ParserVersion,
            ParsedAt = _clock.UtcNow,
            Metrics = new Dictionary<string, string>
            {
                ["confidence"] = confidence.ToString("0.###", CultureInfo.InvariantCulture)
            }
        };

        var classification = _classifier.Classify(textRecord, document.Metadata);
        var updatedDocument = document with
        {
            Status = DocumentStatus.Parsed,
            Kind = classification.Kind,
            Ticker = classification.Ticker ?? document.Ticker,
            PeriodTag = classification.PeriodTag,
            Confidence = decimal.Round((decimal)Math.Clamp((double)classification.Confidence, 0d, 1d), 3),
            ParserVersion = request.ParserVersion,
            ParsedAt = textRecord.ParsedAt,
            OcrUsed = usedOcr,
            Pages = textRecord.Pages
        };

        await _documentRepository.SaveTextAsync(textRecord, ct).ConfigureAwait(false);
        updatedDocument = await _documentRepository.UpdateAsync(updatedDocument, ct).ConfigureAwait(false);

        return new ParseResult
        {
            Success = true,
            RequiresRetry = false,
            TextRecord = textRecord,
            Document = updatedDocument
        };
    }

    private static string SerializeTables(IReadOnlyList<IReadOnlyList<string>> tables)
    {
        if (tables is null || tables.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(tables, SerializerOptions);
    }
}
