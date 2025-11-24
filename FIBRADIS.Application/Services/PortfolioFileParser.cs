using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FIBRADIS.Application.Exceptions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class PortfolioFileParser : IPortfolioFileParser
{
    private const long MaxFileSizeBytes = 2L * 1024 * 1024;
    private const int MaxRowCount = 5000;

    private static readonly Dictionary<string, string> HeaderMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fibra"] = "Ticker",
        ["ticker"] = "Ticker",
        ["emisora"] = "Ticker",
        ["cantidad"] = "Qty",
        ["titulos"] = "Qty",
        ["qty"] = "Qty",
        ["costopromedio"] = "AvgCost",
        ["ctoprom"] = "AvgCost",
        ["ctopromedio"] = "AvgCost",
        ["costoprom"] = "AvgCost",
        ["preciopromedio"] = "AvgCost",
        ["avgcost"] = "AvgCost"
    };

    private readonly ILogger<PortfolioFileParser>? _logger;

    public PortfolioFileParser(ILogger<PortfolioFileParser>? logger = null)
    {
        _logger = logger;
    }

    public async Task<(IEnumerable<NormalizedRow> Rows, IEnumerable<ValidationIssue> Issues)> ParseAsync(
        Stream fileStream,
        string fileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        ct.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new UnsupportedFormatException($"El archivo '{fileName}' no tiene una extensión válida.");
        }

        ParseOutcome outcome;
        long bytes;
        switch (extension)
        {
            case ".csv":
            {
                var (stream, length) = await EnsureSeekableStreamAsync(fileStream, MaxFileSizeBytes, forceCopy: false, ct)
                    .ConfigureAwait(false);
                bytes = length;
                outcome = await ParseCsvAsync(stream, ct).ConfigureAwait(false);
                break;
            }

            case ".xlsx":
            {
                var (stream, length) = await EnsureSeekableStreamAsync(fileStream, MaxFileSizeBytes, forceCopy: true, ct)
                    .ConfigureAwait(false);
                bytes = length;
                outcome = ParseXlsx(stream, ct);
                break;
            }

            default:
                throw new UnsupportedFormatException($"El formato '{extension}' no es soportado.");
        }

        _logger?.LogInformation(
            "Parseo de portafolio completado para {File}. Bytes={Bytes}, FilasValidas={Rows}, FilasIgnoradas={Ignored}, Issues={Issues}",
            fileName,
            bytes,
            outcome.Rows.Count,
            outcome.IgnoredRows,
            outcome.Issues.Count);

        return (outcome.Rows, outcome.Issues);
    }

    private static async Task<(Stream Stream, long Length)> EnsureSeekableStreamAsync(
        Stream source,
        long maxSize,
        bool forceCopy,
        CancellationToken ct)
    {
        if (!forceCopy && source.CanSeek)
        {
            if (source.Length > maxSize)
            {
                throw new InvalidDataException("El archivo excede el tamaño máximo permitido de 2 MB.");
            }

            source.Position = 0;
            return (source, source.Length);
        }

        var memory = new MemoryStream();
        await CopyWithLimitAsync(source, memory, maxSize, ct).ConfigureAwait(false);
        memory.Position = 0;
        return (memory, memory.Length);
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxSize, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxSize)
            {
                throw new InvalidDataException("El archivo excede el tamaño máximo permitido de 2 MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private async Task<ParseOutcome> ParseCsvAsync(Stream stream, CancellationToken ct)
    {
        var issues = new List<ValidationIssue>();
        var aggregates = new Dictionary<string, (decimal Qty, decimal AvgCost)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
        if (headerLine is null)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "El archivo está vacío.",
                Severity = "Warning"
            });
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var headers = ParseCsvLine(headerLine);
        var headerResult = BuildHeaderMapping(headers, issues);
        if (!headerResult.IsValid)
        {
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var rowNumber = 1;
        var processedRows = 0;
        var ignoredRows = 0;
        var validRows = 0;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            rowNumber++;
            if (line is null)
            {
                break;
            }

            if (processedRows >= MaxRowCount)
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = rowNumber,
                    Field = string.Empty,
                    Message = $"Se excedió el límite máximo de filas ({MaxRowCount}).",
                    Severity = "Error"
                });
                break;
            }

            var values = ParseCsvLine(line);
            if (IsRowEmpty(values))
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = rowNumber,
                    Field = string.Empty,
                    Message = "Fila vacía ignorada.",
                    Severity = "Warning"
                });
                ignoredRows++;
                continue;
            }

            processedRows++;
            if (!TryProcessRow(rowNumber, values, headerResult.Mapping, issues, aggregates, order, ref validRows))
            {
                ignoredRows++;
            }
        }

        if (processedRows == 0 && validRows == 0)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "El archivo está vacío.",
                Severity = "Warning"
            });
        }

        var rows = order.Select(ticker => new NormalizedRow
        {
            Ticker = ticker.ToUpperInvariant(),
            Qty = aggregates[ticker].Qty,
            AvgCost = Math.Round(aggregates[ticker].AvgCost, 6)
        }).ToList();

        return new ParseOutcome(rows, issues, processedRows, ignoredRows);
    }

    private ParseOutcome ParseXlsx(Stream stream, CancellationToken ct)
    {
        var issues = new List<ValidationIssue>();
        var aggregates = new Dictionary<string, (decimal Qty, decimal AvgCost)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("El libro no contiene datos.");
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();
        if (sheets.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "El archivo está vacío.",
                Severity = "Warning"
            });
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, "Hoja1", StringComparison.OrdinalIgnoreCase))
                    ?? sheets.First();
        _logger?.LogInformation("Procesando hoja {SheetName}", sheet.Name?.Value);

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "El archivo está vacío.",
                Severity = "Warning"
            });
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var rows = sheetData.Elements<Row>().ToList();
        if (rows.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "El archivo está vacío.",
                Severity = "Warning"
            });
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var headerValues = ExtractRowValues(rows[0], workbookPart);
        var headerResult = BuildHeaderMapping(headerValues, issues);
        if (!headerResult.IsValid)
        {
            return new ParseOutcome(new List<NormalizedRow>(), issues, 0, 0);
        }

        var processedRows = 0;
        var ignoredRows = 0;
        var validRows = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i];
            var rowNumber = (int)(row.RowIndex?.Value ?? (uint)(i + 1));

            if (processedRows >= MaxRowCount)
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = rowNumber,
                    Field = string.Empty,
                    Message = $"Se excedió el límite máximo de filas ({MaxRowCount}).",
                    Severity = "Error"
                });
                break;
            }

            var values = ExtractRowValues(row, workbookPart);
            if (IsRowEmpty(values))
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = rowNumber,
                    Field = string.Empty,
                    Message = "Fila vacía ignorada.",
                    Severity = "Warning"
                });
                ignoredRows++;
                continue;
            }

            processedRows++;
            if (!TryProcessRow(rowNumber, values, headerResult.Mapping, issues, aggregates, order, ref validRows))
            {
                ignoredRows++;
            }
        }

        if (processedRows == 0 && validRows == 0)
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = 0,
                Field = string.Empty,
                Message = "Sin datos en la hoja.",
                Severity = "Warning"
            });
        }

        var rowsResult = order.Select(ticker => new NormalizedRow
        {
            Ticker = ticker.ToUpperInvariant(),
            Qty = aggregates[ticker].Qty,
            AvgCost = Math.Round(aggregates[ticker].AvgCost, 6)
        }).ToList();

        return new ParseOutcome(rowsResult, issues, processedRows, ignoredRows);
    }

    private static HeaderResult BuildHeaderMapping(IReadOnlyList<string> headers, List<ValidationIssue> issues)
    {
        var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var sanitized = SanitizeHeader(header);
            if (HeaderMappings.TryGetValue(sanitized, out var canonical) && !mapping.ContainsKey(canonical))
            {
                mapping[canonical] = i;
            }
        }

        var required = new[] { "Ticker", "Qty", "AvgCost" };
        var valid = true;
        foreach (var field in required)
        {
            if (!mapping.ContainsKey(field))
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = 1,
                    Field = field,
                    Message = $"La columna requerida '{field}' no fue encontrada.",
                    Severity = "Error"
                });
                valid = false;
            }
        }

        return new HeaderResult(mapping, valid);
    }

    private static bool TryProcessRow(
        int rowNumber,
        IReadOnlyList<string> values,
        Dictionary<string, int> headerMapping,
        List<ValidationIssue> issues,
        Dictionary<string, (decimal Qty, decimal AvgCost)> aggregates,
        List<string> order,
        ref int validRows)
    {
        var ticker = GetValue(values, headerMapping, "Ticker").Trim();
        var qtyValue = GetValue(values, headerMapping, "Qty");
        var avgCostValue = GetValue(values, headerMapping, "AvgCost");

        var hasError = false;
        if (string.IsNullOrWhiteSpace(ticker))
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = rowNumber,
                Field = "Ticker",
                Message = "El ticker es obligatorio.",
                Severity = "Error"
            });
            hasError = true;
        }

        var normalizedTicker = ticker.ToUpperInvariant();

        if (!TryParsePositiveDecimal(qtyValue, out var qty, out var qtyError))
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = rowNumber,
                Field = "Qty",
                Message = qtyError,
                Severity = "Error"
            });
            hasError = true;
        }

        if (!TryParsePositiveDecimal(avgCostValue, out var avgCost, out var avgCostError))
        {
            issues.Add(new ValidationIssue
            {
                RowNumber = rowNumber,
                Field = "AvgCost",
                Message = avgCostError,
                Severity = "Error"
            });
            hasError = true;
        }

        if (hasError)
        {
            return false;
        }

        if (aggregates.TryGetValue(normalizedTicker, out var existing))
        {
            var newQty = existing.Qty + qty;
            var weightedAvg = ((existing.AvgCost * existing.Qty) + (avgCost * qty)) / newQty;
            aggregates[normalizedTicker] = (newQty, weightedAvg);
            issues.Add(new ValidationIssue
            {
                RowNumber = rowNumber,
                Field = "Ticker",
                Message = "Ticker duplicado. Se agregaron los valores.",
                Severity = "Warning"
            });
        }
        else
        {
            aggregates[normalizedTicker] = (qty, avgCost);
            order.Add(normalizedTicker);
        }

        validRows++;
        return true;
    }

    private static string GetValue(IReadOnlyList<string> values, Dictionary<string, int> headerMapping, string field)
    {
        if (headerMapping.TryGetValue(field, out var index) && index < values.Count)
        {
            return values[index];
        }

        return string.Empty;
    }

    private static bool IsRowEmpty(IReadOnlyList<string> values)
    {
        return values.Count == 0 || values.All(v => string.IsNullOrWhiteSpace(v));
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }

    private static IReadOnlyList<string> ExtractRowValues(Row row, WorkbookPart workbookPart)
    {
        var values = new SortedDictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetColumnIndex(cell.CellReference?.Value);
            values[columnIndex] = GetCellValue(cell, workbookPart);
        }

        if (values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var lastIndex = values.Keys.Last();
        var result = new string[lastIndex + 1];
        foreach (var pair in values)
        {
            result[pair.Key] = pair.Value;
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] ??= string.Empty;
        }

        return result;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        var match = Regex.Match(cellReference, "^[A-Z]+");
        if (!match.Success)
        {
            return 0;
        }

        var column = match.Value;
        var sum = 0;
        for (var i = 0; i < column.Length; i++)
        {
            sum *= 26;
            sum += column[i] - 'A' + 1;
        }

        return sum - 1;
    }

    private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        var value = cell.InnerText ?? string.Empty;
        if (cell.DataType is null)
        {
            return value;
        }

        var dataType = cell.DataType.Value;

        return dataType switch
        {
            CellValues.SharedString => GetSharedString(workbookPart, value),
            CellValues.InlineString => cell.InnerText,
            _ => value
        };
    }

    private static string GetSharedString(WorkbookPart workbookPart, string index)
    {
        if (!int.TryParse(index, out var idx))
        {
            return string.Empty;
        }

        var sharedTable = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (sharedTable is null || idx < 0 || idx >= sharedTable.Count())
        {
            return string.Empty;
        }

        return sharedTable.ElementAt(idx).InnerText ?? string.Empty;
    }

    private static string SanitizeHeader(string header)
    {
        var normalized = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static bool TryParsePositiveDecimal(string value, out decimal result, out string error)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0;
            error = "El valor es obligatorio.";
            return false;
        }

        var styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign;
        var cultures = new[]
        {
            CultureInfo.InvariantCulture,
            new CultureInfo("es-MX"),
            CultureInfo.CurrentCulture
        };

        foreach (var culture in cultures)
        {
            if (decimal.TryParse(value, styles, culture, out result))
            {
                if (result > 0)
                {
                    error = string.Empty;
                    return true;
                }

                error = "El valor debe ser mayor que cero.";
                result = 0;
                return false;
            }
        }

        var sanitized = value.Replace(".", string.Empty).Replace(',', '.');
        if (decimal.TryParse(sanitized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
        {
            if (result > 0)
            {
                error = string.Empty;
                return true;
            }

            error = "El valor debe ser mayor que cero.";
            result = 0;
            return false;
        }

        result = 0;
        error = "El valor no es un número válido.";
        return false;
    }

    private sealed record HeaderResult(Dictionary<string, int> Mapping, bool IsValid);

    private sealed class ParseOutcome
    {
        public ParseOutcome(List<NormalizedRow> rows, List<ValidationIssue> issues, int processedRows, int ignoredRows)
        {
            Rows = rows;
            Issues = issues;
            ProcessedRows = processedRows;
            IgnoredRows = ignoredRows;
        }

        public List<NormalizedRow> Rows { get; }
        public List<ValidationIssue> Issues { get; }
        public int ProcessedRows { get; }
        public int IgnoredRows { get; }
    }
}
