using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services.Documents;

public sealed class DocumentDownloadService : IDocumentDownloader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static readonly int MaxBytes = 20 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IClock _clock;
    private readonly ILogger<DocumentDownloadService> _logger;

    public DocumentDownloadService(HttpClient httpClient, IClock clock, ILogger<DocumentDownloadService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient.Timeout = Timeout;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FIBRADISBot (+contacto)");
    }

    public async Task<DownloadResult> DownloadAsync(DocumentDownloadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required", nameof(request));
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid document url {request.Url}");
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Accept.Clear();
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new DownloadResult
            {
                Success = true,
                NotModified = true,
                Document = new DocumentRecord
                {
                    DocumentId = request.DocumentId,
                    Url = request.Url,
                    Status = DocumentStatus.Downloaded,
                    DownloadedAt = _clock.UtcNow
                }
            };
        }

        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
        {
            return new DownloadResult
            {
                Ignored = true,
                FailureReason = $"Documento no disponible ({(int)response.StatusCode})"
            };
        }

        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength ?? 0;
        if (contentLength > MaxBytes)
        {
            return new DownloadResult
            {
                Ignored = true,
                FailureReason = $"Documento excede tamaño máximo ({contentLength} bytes)"
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        long total = 0;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                return new DownloadResult
                {
                    Ignored = true,
                    FailureReason = $"Documento excede tamaño máximo ({total} bytes)"
                };
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        var bytes = memory.ToArray();
        var hash = ComputeHash(bytes);
        var publishedAt = ParsePublishedAt(response.Headers.Date);
        var etag = response.Headers.ETag?.Tag;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";

        return new DownloadResult
        {
            Success = true,
            Binary = new DocumentBinary
            {
                DocumentId = request.DocumentId,
                Hash = hash,
                Content = bytes,
                ContentType = contentType,
                ContentLength = total,
                IsImageBased = false,
                DownloadedAt = _clock.UtcNow
            },
            Document = new DocumentRecord
            {
                DocumentId = request.DocumentId,
                Url = request.Url,
                ContentType = contentType,
                Hash = hash,
                DiscoveredAt = _clock.UtcNow,
                DownloadedAt = _clock.UtcNow,
                Status = DocumentStatus.Downloaded,
                PublishedAt = publishedAt,
                Provenance = new DocumentProvenance
                {
                    ETag = etag,
                    RobotsOk = true
                },
                Confidence = 0.5m
            }
        };
    }

    private static string ComputeHash(ReadOnlySpan<byte> content)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(content.ToArray());
        return Convert.ToHexString(hash);
    }

    private static DateTimeOffset? ParsePublishedAt(DateTimeOffset? headerDate)
    {
        if (headerDate is null)
        {
            return null;
        }

        return headerDate.Value;
    }
}
