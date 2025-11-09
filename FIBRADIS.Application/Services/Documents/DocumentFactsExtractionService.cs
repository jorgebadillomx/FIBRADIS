using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Application.Services.Documents;

public sealed class DocumentFactsExtractionService : IFactsExtractor
{
    private readonly IPdfFactsParserService _pdfFactsParserService;
    private readonly IFactsRepository _factsRepository;
    private readonly IDocumentRepository _documentRepository;

    public DocumentFactsExtractionService(
        IPdfFactsParserService pdfFactsParserService,
        IFactsRepository factsRepository,
        IDocumentRepository documentRepository)
    {
        _pdfFactsParserService = pdfFactsParserService ?? throw new ArgumentNullException(nameof(pdfFactsParserService));
        _factsRepository = factsRepository ?? throw new ArgumentNullException(nameof(factsRepository));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
    }

    public async Task<FactsResult> ExtractAsync(DocumentFactsRequest request, CancellationToken ct)
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
            throw new InvalidOperationException($"Document {request.DocumentId} missing hash for facts extraction.");
        }

        if (string.IsNullOrWhiteSpace(document.Ticker))
        {
            throw new InvalidOperationException($"Document {request.DocumentId} missing ticker classification.");
        }

        var parseRequest = new ParseFactsRequest
        {
            DocumentId = request.DocumentId,
            FibraTicker = document.Ticker!,
            Url = document.Url,
            Hash = document.Hash!,
            ParserVersion = request.ParserVersion
        };

        var result = await _pdfFactsParserService.ParseAsync(parseRequest, ct).ConfigureAwait(false);
        var factsRecord = await _factsRepository
            .GetDocumentFactsAsync(request.DocumentId, request.ParserVersion, document.Hash!, ct)
            .ConfigureAwait(false);

        var updatedDocument = document with
        {
            Status = DocumentStatus.FactsExtracted,
            ParserVersion = request.ParserVersion
        };

        await _documentRepository.UpdateAsync(updatedDocument, ct).ConfigureAwait(false);

        var requiresReview = factsRecord?.RequiresReview ?? result.Score < 70;

        return new FactsResult
        {
            Success = true,
            RequiresReview = requiresReview,
            Score = result.Score,
            Facts = factsRecord,
            Document = updatedDocument
        };
    }
}
