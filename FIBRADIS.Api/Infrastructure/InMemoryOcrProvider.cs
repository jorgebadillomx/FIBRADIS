using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryOcrProvider : IOcrProvider
{
    private readonly ConcurrentDictionary<string, OcrExtractionResult> _results = new();

    public Task<OcrExtractionResult> ExtractAsync(byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var key = ComputeKey(content);
        if (_results.TryGetValue(key, out var result))
        {
            return Task.FromResult(Clone(result));
        }

        return Task.FromResult(new OcrExtractionResult(string.Empty, Array.Empty<IReadOnlyList<string>>(), 0));
    }

    public void SetResult(byte[] content, OcrExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(content);
        _results[ComputeKey(content)] = Clone(result);
    }

    private static string ComputeKey(byte[] content)
    {
        return Convert.ToBase64String(content);
    }

    private static OcrExtractionResult Clone(OcrExtractionResult result)
    {
        var tables = result.Tables.Select(row => (IReadOnlyList<string>)row.ToArray()).ToArray();
        return new OcrExtractionResult(result.Text, tables, result.Confidence);
    }
}
