using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class SimplePdfTextExtractor : IPdfTextExtractor
{
    public Task<PdfTextExtraction> ExtractAsync(byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = Encoding.UTF8.GetString(content);
        var tables = ParseTables(text);
        return Task.FromResult(new PdfTextExtraction(text, tables, false, 1));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseTables(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var cells = SplitRow(line);
            if (cells is not null && cells.Length > 1)
            {
                rows.Add(cells);
            }
        }

        return rows;
    }

    private static string[]? SplitRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (line.Contains('|'))
        {
            return line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (line.Contains(';'))
        {
            return line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return null;
    }
}
