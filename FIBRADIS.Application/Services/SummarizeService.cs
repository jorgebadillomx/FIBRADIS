using System.Collections.Generic;
using System.Linq;
using System.Text;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Exceptions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FIBRADIS.Application.Services;

public sealed class SummarizeService : ISummarizeService
{
    private readonly ISummaryRepository _summaryRepository;
    private readonly IFactsRepository _factsRepository;
    private readonly ILLMUsageTracker _usageTracker;
    private readonly IAuditService _auditService;
    private readonly IClock _clock;
    private readonly SummarizeServiceOptions _options;
    private readonly ILogger<SummarizeService> _logger;

    public SummarizeService(
        ISummaryRepository summaryRepository,
        IFactsRepository factsRepository,
        ILLMUsageTracker usageTracker,
        IAuditService auditService,
        IClock clock,
        IOptions<SummarizeServiceOptions> options,
        ILogger<SummarizeService> logger)
    {
        _summaryRepository = summaryRepository ?? throw new ArgumentNullException(nameof(summaryRepository));
        _factsRepository = factsRepository ?? throw new ArgumentNullException(nameof(factsRepository));
        _usageTracker = usageTracker ?? throw new ArgumentNullException(nameof(usageTracker));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<IReadOnlyList<DocumentSummaryCandidate>> GetPendingDocumentsAsync(string parserVersion, CancellationToken cancellationToken)
        => _summaryRepository.GetPendingDocumentsAsync(parserVersion, cancellationToken);

    public async Task<SummarizeResult> SummarizeAsync(DocumentSummaryCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        if (candidate.Facts is null)
        {
            throw new MissingFactsException($"Document {candidate.DocumentId} does not have parsed facts.");
        }

        if (candidate.Facts.FieldsFound < _options.MinFactsRequired)
        {
            throw new MissingFactsException($"Document {candidate.DocumentId} does not have enough facts for summarization.");
        }

        var key = ResolveKey(candidate.ByoKey);
        var provider = string.IsNullOrWhiteSpace(candidate.Provider) ? _options.Provider : candidate.Provider;

        _logger.LogInformation(
            "Generating summaries for document {DocumentId} ({Ticker}-{Period}) using provider {Provider} with BYO={HasByo}",
            candidate.DocumentId,
            candidate.FibraTicker,
            candidate.PeriodTag,
            provider,
            !string.IsNullOrWhiteSpace(candidate.ByoKey));

        var facts = await _factsRepository.GetDocumentFactsAsync(candidate.DocumentId, candidate.ParserVersion, candidate.Hash, cancellationToken).ConfigureAwait(false)
                    ?? candidate.Facts;

        if (facts.FieldsFound < _options.MinFactsRequired)
        {
            throw new MissingFactsException($"Document {candidate.DocumentId} does not meet facts threshold after refresh.");
        }

        var publicSummaryText = BuildPublicSummary(candidate, facts);
        var privateSummaryText = BuildPrivateSummary(candidate, facts);

        var publicTokens = EstimateTokens(publicSummaryText);
        var privateTokens = EstimateTokens(privateSummaryText);
        var totalTokens = publicTokens + privateTokens;

        if (candidate.RemainingTokenQuota < totalTokens)
        {
            throw new QuotaExceededException($"User {candidate.UserId ?? "system"} exceeded LLM quota while summarizing {candidate.DocumentId}.");
        }

        var cost = Math.Round(totalTokens / 1000m * _options.CostPerThousandTokensUsd, 6, MidpointRounding.AwayFromZero);

        var timestamp = _clock.UtcNow;
        var createdBy = string.IsNullOrWhiteSpace(candidate.UploadedBy) ? "system" : candidate.UploadedBy!;

        var publicSummary = new SummaryRecord
        {
            FibraTicker = candidate.FibraTicker,
            PeriodTag = candidate.PeriodTag,
            Type = SummaryType.Public,
            Content = publicSummaryText,
            TokensUsed = publicTokens,
            CostUsd = Math.Round(cost * publicTokens / Math.Max(totalTokens, 1), 6, MidpointRounding.AwayFromZero),
            SourceDocumentId = candidate.DocumentId,
            CreatedAt = timestamp,
            CreatedBy = createdBy
        };

        var privateSummary = new SummaryRecord
        {
            FibraTicker = candidate.FibraTicker,
            PeriodTag = candidate.PeriodTag,
            Type = SummaryType.Private,
            Content = privateSummaryText,
            TokensUsed = privateTokens,
            CostUsd = Math.Round(cost * privateTokens / Math.Max(totalTokens, 1), 6, MidpointRounding.AwayFromZero),
            SourceDocumentId = candidate.DocumentId,
            CreatedAt = timestamp,
            CreatedBy = createdBy
        };

        await _summaryRepository.SaveSummaryAsync(publicSummary, cancellationToken).ConfigureAwait(false);
        await _summaryRepository.SaveSummaryAsync(privateSummary, cancellationToken).ConfigureAwait(false);
        await _summaryRepository.MarkDocumentSummarizedAsync(candidate.DocumentId, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(candidate.UserId))
        {
            await _usageTracker.RecordUsageAsync(candidate.UserId!, provider, totalTokens, cost, cancellationToken).ConfigureAwait(false);
            await _auditService.RecordAsync(
                new AuditEntry(
                    candidate.UserId!,
                    "summarize.generated",
                    "success",
                    candidate.SourceUrl ?? "system",
                    new Dictionary<string, object?>
                    {
                        ["documentId"] = candidate.DocumentId,
                        ["ticker"] = candidate.FibraTicker,
                        ["period"] = candidate.PeriodTag,
                        ["tokens"] = totalTokens,
                        ["costUsd"] = cost,
                        ["provider"] = provider,
                        ["byo"] = !string.IsNullOrWhiteSpace(candidate.ByoKey),
                        ["keyUsed"] = key.Length
                    }),
                cancellationToken).ConfigureAwait(false);
        }

        var shouldTriggerNews = DetectRelevantEvent(privateSummaryText);

        return new SummarizeResult(publicSummary, privateSummary, totalTokens, cost, shouldTriggerNews);
    }

    private string ResolveKey(string? byoKey)
    {
        if (!string.IsNullOrWhiteSpace(byoKey))
        {
            if (byoKey.Length < 12 || byoKey.Any(char.IsWhiteSpace))
            {
                throw new InvalidByoKeyException("Provided BYO key is invalid.");
            }

            return byoKey;
        }

        if (string.IsNullOrWhiteSpace(_options.SystemKey))
        {
            throw new InvalidOperationException("System LLM key is not configured.");
        }

        return _options.SystemKey;
    }

    private static int EstimateTokens(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Math.Max(16, (int)Math.Ceiling(words.Length * 1.3));
    }

    private string BuildPublicSummary(DocumentSummaryCandidate candidate, DocumentFactsRecord facts)
    {
        var builder = new StringBuilder();
        builder.Append($"Resumen trimestral de {candidate.FibraTicker} ({candidate.PeriodTag}). ");
        if (facts.NavPerCbfi is not null)
        {
            builder.Append($"NAV/CBFI: {facts.NavPerCbfi:0.00}. ");
        }
        if (facts.Noi is not null)
        {
            builder.Append($"NOI: {facts.Noi:0.##} mdp. ");
        }
        if (facts.Occupancy is not null)
        {
            builder.Append($"Ocupación: {facts.Occupancy:P0}. ");
        }
        if (facts.Dividends is not null)
        {
            builder.Append($"Dividendos: {facts.Dividends:0.##} por CBFI. ");
        }

        builder.Append("Fuente: reporte financiero resumido automáticamente.");
        return builder.ToString().Trim();
    }

    private string BuildPrivateSummary(DocumentSummaryCandidate candidate, DocumentFactsRecord facts)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Reporte detallado de {candidate.FibraTicker} para {candidate.PeriodTag}.");
        builder.AppendLine($"NAV/CBFI: {FormatDecimal(facts.NavPerCbfi)}");
        builder.AppendLine($"NOI: {FormatDecimal(facts.Noi)} mdp");
        builder.AppendLine($"AFFO: {FormatDecimal(facts.Affo)} mdp");
        builder.AppendLine($"LTV: {FormatPercentage(facts.Ltv)}");
        builder.AppendLine($"Ocupación: {FormatPercentage(facts.Occupancy)}");
        builder.AppendLine($"Dividendos distribuidos: {FormatDecimal(facts.Dividends)} por CBFI");
        builder.AppendLine();
        builder.AppendLine("Hechos relevantes detectados:");

        if (!string.IsNullOrWhiteSpace(candidate.DocumentExcerpt))
        {
            builder.AppendLine(candidate.DocumentExcerpt.Trim());
        }
        else
        {
            builder.AppendLine("No se detectaron hechos adicionales.");
        }

        builder.AppendLine();
        builder.AppendLine("Generado automáticamente para revisión interna.");
        return builder.ToString().Trim();
    }

    private static string FormatDecimal(decimal? value)
        => value is null ? "N/D" : value.Value.ToString("0.##");

    private static string FormatPercentage(decimal? value)
        => value is null ? "N/D" : value.Value.ToString("P0");

    private bool DetectRelevantEvent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return _options.EventKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
