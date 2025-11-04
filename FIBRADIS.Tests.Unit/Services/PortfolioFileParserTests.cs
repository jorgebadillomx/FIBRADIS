using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FIBRADIS.Application.Services;

namespace FIBRADIS.Tests.Unit.Services;

public class PortfolioFileParserTests
{
    [Fact]
    public async Task ParseCsv_ValidFile_ReturnsRows()
    {
        var parser = new PortfolioFileParser();
        var content = "FIBRA,Cantidad,Costo Promedio\nFUNO11,10,12.5\nFIBRAMQ12,5,11.2";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "portfolio.csv");

        var normalized = rows.ToList();
        Assert.Equal(2, normalized.Count);
        Assert.All(normalized, row => Assert.True(row.Qty > 0));
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseXlsx_ValidFile_ReturnsRows()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos", "Cto. Prom." },
                new[] { "FUNO11", "10", "12.5" },
                new[] { "FIBRAMQ12", "5", "11.2" }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "portfolio.xlsx");

        Assert.Equal(2, rows.Count());
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseCsv_DuplicateTickers_AggregatesAndWarns()
    {
        var parser = new PortfolioFileParser();
        var content = "Ticker,Cantidad,Costo Promedio\nFUNO11,10,10\nFUNO11,5,20";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "dup.csv");

        var row = Assert.Single(rows);
        Assert.Equal("FUNO11", row.Ticker);
        Assert.Equal(15, row.Qty);
        Assert.Equal(13.333333m, Math.Round(row.AvgCost, 6));
        var warning = Assert.Single(issues);
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal("Ticker", warning.Field);
    }

    [Fact]
    public async Task ParseCsv_InvalidValues_ReportIssues()
    {
        var parser = new PortfolioFileParser();
        var content = "Ticker,Cantidad,Costo Promedio\nFUNO11,0,10\nFIBRAMQ12,5,-1";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "invalid.csv");

        Assert.Empty(rows);
        Assert.Equal(2, issues.Count());
        Assert.All(issues, i => Assert.Equal("Error", i.Severity));
    }

    [Fact]
    public async Task ParseCsv_MissingHeader_AddsIssue()
    {
        var parser = new PortfolioFileParser();
        var content = "Ticker,Cantidad\nFUNO11,1";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "missing.csv");

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Field == "AvgCost" && issue.Severity == "Error");
    }

    [Fact]
    public async Task ParseCsv_ExtraColumns_Ignored()
    {
        var parser = new PortfolioFileParser();
        var content = "Ticker,Cantidad,Costo Promedio,Extra\nFUNO11,10,12.5,foo";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "extra.csv");

        var row = Assert.Single(rows);
        Assert.Equal("FUNO11", row.Ticker);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseCsv_HeaderVariations_Succeeds()
    {
        var parser = new PortfolioFileParser();
        var content = "fibra,Títulos,COSTO PROMEDIO\nFIBRAPL,3,100";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var (rows, issues) = await parser.ParseAsync(stream, "variations.csv");

        var row = Assert.Single(rows);
        Assert.Equal("FIBRAPL", row.Ticker);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseXlsx_DecimalCultures_Succeeds()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos", "Cto. Prom." },
                new[] { "FUNO11", "10", "1,23" },
                new[] { "FIBRAMQ12", "5", "1.23" }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "culture.xlsx");

        Assert.Equal(2, rows.Count());
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseXlsx_SelectsHoja1WhenMultipleSheets()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Otra"] = new[]
            {
                new[] { "Foo", "Bar", "Baz" },
                new[] { "1", "2", "3" }
            },
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos", "Cto. Prom." },
                new[] { "FUNO11", "10", "12" }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "multisheet.xlsx");

        Assert.Single(rows);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ParseCsv_CancellationRequested_Throws()
    {
        var parser = new PortfolioFileParser();
        var content = "Ticker,Cantidad,Costo Promedio\nFUNO11,10,12";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => parser.ParseAsync(stream, "cancel.csv", cts.Token));
    }

    [Fact]
    public async Task ParseCsv_FileTooLarge_Throws()
    {
        var parser = new PortfolioFileParser();
        await using var stream = new MemoryStream(new byte[(2 * 1024 * 1024) + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => parser.ParseAsync(stream, "large.csv"));
    }

    [Fact]
    public async Task ParseCsv_EmptyFile_Warns()
    {
        var parser = new PortfolioFileParser();
        await using var stream = new MemoryStream();

        var (rows, issues) = await parser.ParseAsync(stream, "empty.csv");

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Severity == "Warning");
    }

    [Fact]
    public async Task ParseXlsx_HeaderOnly_Warns()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos", "Cto. Prom." }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "headeronly.xlsx");

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Severity == "Warning");
    }

    [Fact]
    public async Task ParseXlsx_InvalidNumbers_ReportIssues()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos", "Cto. Prom." },
                new[] { "FUNO11", "0", "10" },
                new[] { "FIBRAMQ12", "5", "-1" }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "invalid.xlsx");

        Assert.Empty(rows);
        Assert.Equal(2, issues.Count(issue => issue.Severity == "Error"));
    }

    [Fact]
    public async Task ParseXlsx_MissingHeader_ReportIssue()
    {
        var parser = new PortfolioFileParser();
        await using var stream = CreateXlsx(new Dictionary<string, string[][]>
        {
            ["Hoja1"] = new[]
            {
                new[] { "Emisora", "Títulos" },
                new[] { "FUNO11", "10" }
            }
        });

        var (rows, issues) = await parser.ParseAsync(stream, "missingheader.xlsx");

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Field == "AvgCost" && issue.Severity == "Error");
    }

    [Fact]
    public async Task ParseCsv_RowLimit_StopsProcessing()
    {
        var parser = new PortfolioFileParser();
        var sb = new StringBuilder();
        sb.AppendLine("Ticker,Cantidad,Costo Promedio");
        for (var i = 0; i < 5001; i++)
        {
            sb.AppendLine($"FUNO{i},1,1");
        }

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var (_, issues) = await parser.ParseAsync(stream, "limit.csv");

        Assert.Contains(issues, issue => issue.Severity == "Error" && issue.Message.Contains("límite"));
    }

    private static MemoryStream CreateXlsx(Dictionary<string, string[][]> sheets)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheetsElement = workbookPart.Workbook.AppendChild(new Sheets());

            uint sheetId = 1;
            foreach (var entry in sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                worksheetPart.Worksheet = new Worksheet(sheetData);

                var sheet = new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId++,
                    Name = entry.Key
                };

                sheetsElement.Append(sheet);

                foreach (var rowValues in entry.Value)
                {
                    var row = new Row();
                    foreach (var cellValue in rowValues)
                    {
                        row.Append(CreateTextCell(cellValue));
                    }

                    sheetData.Append(row);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static Cell CreateTextCell(string value)
    {
        return new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value))
        };
    }
}
