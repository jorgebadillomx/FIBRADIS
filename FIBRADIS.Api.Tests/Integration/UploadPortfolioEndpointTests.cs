using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Linq;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Api.Tests.Integration;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FIBRADIS.Api.Tests.Integration;

public class UploadPortfolioEndpointTests : IClassFixture<ApiApplicationFactory>
{
    private readonly ApiApplicationFactory _factory;

    public UploadPortfolioEndpointTests(ApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadPortfolio_HappyPath_ReturnsSnapshot()
    {
        using var client = CreateAuthenticatedClient();
        ConfigureMarketData(scope =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ISecurityCatalog>() as InMemorySecurityCatalog;
            catalog!.SetPrice("FUNO11", 25.10m);
            catalog.SetPrice("FIBRATC14", 22.03m);

            var distribution = scope.ServiceProvider.GetRequiredService<IDistributionReader>() as InMemoryDistributionReader;
            distribution!.SetYield("FUNO11", 0.0673m, 0.0681m);
            distribution.SetYield("FIBRATC14", 0.0643m, 0.0643m);
        });

        var csv = "FIBRA,Cantidad,Costo Promedio\nFUNO11,100,24.50\nFIBRATC14,45,21.64";
        using var content = CreateCsvContent(csv);

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPortfolioResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Imported);
        Assert.Equal(0, payload.Ignored);
        Assert.Equal("FUNO11", payload.Positions[0].Ticker);
        Assert.Equal("FIBRATC14", payload.Positions[1].Ticker);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestId));
    }

    [Fact]
    public async Task UploadPortfolio_NoValidRows_ReturnsBadRequest()
    {
        using var client = CreateAuthenticatedClient();
        var csv = "FIBRA,Cantidad,Costo Promedio\nFUNO11,0,0";
        using var content = CreateCsvContent(csv);

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_EmptyFile_ReturnsBadRequest()
    {
        using var client = CreateAuthenticatedClient();
        using var content = CreateCsvContent(string.Empty);

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_BadFormat_ReturnsUnsupported()
    {
        using var client = CreateAuthenticatedClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("dummy")), "file", "portfolio.pdf" }
        };

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_TooLarge_ReturnsPayloadTooLarge()
    {
        using var client = CreateAuthenticatedClient();
        var oversized = new string('A', (2 * 1024 * 1024) + 10);
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(oversized)), "file", "portfolio.csv" }
        };

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal((HttpStatusCode)StatusCodes.Status413PayloadTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_ParserIssues_ReturnsIgnored()
    {
        using var client = CreateAuthenticatedClient();
        ConfigureMarketData(scope =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ISecurityCatalog>() as InMemorySecurityCatalog;
            catalog!.SetPrice("FUNO11", 25m);
            var distribution = scope.ServiceProvider.GetRequiredService<IDistributionReader>() as InMemoryDistributionReader;
            distribution!.SetYield("FUNO11", 0.06m, 0.06m);
        });

        var csv = "FIBRA,Cantidad,Costo Promedio\nFUNO11,100,24\nFIBRA999,-1,10";
        using var content = CreateCsvContent(csv);

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPortfolioResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Imported);
        Assert.True(payload.Ignored > 0);
    }

    [Fact]
    public async Task UploadPortfolio_ServiceThrows_ReturnsInternalServerError()
    {
        using var clientFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPortfolioReplaceService>();
                services.AddSingleton<IPortfolioReplaceService, ThrowingReplaceService>();
            });
        });

        using var client = CreateAuthenticatedClient(clientFactory);
        using var content = CreateCsvContent("FIBRA,Cantidad,Costo Promedio\nFUNO11,10,10");

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_RateLimit_ReturnsTooManyRequests()
    {
        using var client = CreateAuthenticatedClient();
        for (var i = 0; i < 5; i++)
        {
            using var attempt = CreateCsvContent("FIBRA,Cantidad,Costo Promedio\nFUNO11,10,10");
            var response = await client.PostAsync("/v1/portfolio/upload", attempt);
            Assert.NotEqual((HttpStatusCode)StatusCodes.Status429TooManyRequests, response.StatusCode);
        }

        using var finalAttempt = CreateCsvContent("FIBRA,Cantidad,Costo Promedio\nFUNO11,10,10");
        var limited = await client.PostAsync("/v1/portfolio/upload", finalAttempt);
        Assert.Equal((HttpStatusCode)StatusCodes.Status429TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task UploadPortfolio_IdempotentHash_ReturnsConsistentSnapshot()
    {
        using var client = CreateAuthenticatedClient();
        ConfigureMarketData(scope =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ISecurityCatalog>() as InMemorySecurityCatalog;
            catalog!.SetPrice("FUNO11", 20m);
            var distribution = scope.ServiceProvider.GetRequiredService<IDistributionReader>() as InMemoryDistributionReader;
            distribution!.SetYield("FUNO11", 0.05m, 0.05m);
        });

        using var firstContent = CreateCsvContent("FIBRA,Cantidad,Costo Promedio\nFUNO11,10,10");
        var first = await client.PostAsync("/v1/portfolio/upload", firstContent);
        using var secondContent = CreateCsvContent("FIBRA,Cantidad,Costo Promedio\nFUNO11,10,10");
        var second = await client.PostAsync("/v1/portfolio/upload", secondContent);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var snapshot1 = await first.Content.ReadFromJsonAsync<UploadPortfolioResponse>();
        var snapshot2 = await second.Content.ReadFromJsonAsync<UploadPortfolioResponse>();

        Assert.NotNull(snapshot1);
        Assert.NotNull(snapshot2);
        Assert.Equal(snapshot1!.Imported, snapshot2!.Imported);
        Assert.Equal(snapshot1.Ignored, snapshot2.Ignored);
        Assert.Equal(snapshot1.Metrics.Value, snapshot2.Metrics.Value);
    }

    [Fact]
    public async Task UploadPortfolio_Xlsx_Hoja1Headers_Succeeds()
    {
        using var client = CreateAuthenticatedClient();
        ConfigureMarketData(scope =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ISecurityCatalog>() as InMemorySecurityCatalog;
            catalog!.SetPrice("FUNO11", 23.1m);
            var distribution = scope.ServiceProvider.GetRequiredService<IDistributionReader>() as InMemoryDistributionReader;
            distribution!.SetYield("FUNO11", 0.06m, 0.06m);
        });

        using var content = CreateXlsxContent(new[] { ("FUNO11", 10m, 21.5m) }, "gbmdatosfibras.xlsx");

        var response = await client.PostAsync("/v1/portfolio/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPortfolioResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Imported);
        Assert.Equal("FUNO11", payload.Positions.Single().Ticker);
    }

    private HttpClient CreateAuthenticatedClient(ApiApplicationFactory? factory = null)
    {
        var targetFactory = factory ?? _factory;
        var client = targetFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sub:user-1;role:user");
        return client;
    }

    private static MultipartFormDataContent CreateCsvContent(string csv, string fileName = "portfolio.csv")
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        return content;
    }

    private static MultipartFormDataContent CreateXlsxContent(IEnumerable<(string Ticker, decimal Qty, decimal AvgCost)> rows, string fileName)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());
            var sheets = document.WorkbookPart!.Workbook.AppendChild(new Sheets());
            var sheet = new Sheet
            {
                Id = document.WorkbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Hoja1"
            };
            sheets.Append(sheet);

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;
            sheetData.AppendChild(CreateRow("Emisora", "Títulos", "Cto. Prom."));
            foreach (var (ticker, qty, avgCost) in rows)
            {
                sheetData.AppendChild(CreateRow(
                    ticker,
                    qty.ToString("0.##", CultureInfo.InvariantCulture),
                    avgCost.ToString("0.##", CultureInfo.InvariantCulture)));
            }

            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        return content;
    }

    private static Row CreateRow(params string[] values)
    {
        var row = new Row();
        foreach (var value in values)
        {
            row.AppendChild(new Cell
            {
                DataType = new EnumValue<CellValues>(CellValues.String),
                CellValue = new CellValue(value)
            });
        }

        return row;
    }

    private void ConfigureMarketData(Action<IServiceScope> configure)
    {
        using var scope = _factory.Services.CreateScope();
        configure(scope);
    }

    private sealed class ThrowingReplaceService : IPortfolioReplaceService
    {
        public Task<UploadPortfolioResponse> ReplaceAsync(string userId, IEnumerable<NormalizedRow> rows, IEnumerable<ValidationIssue> issuesFromParser, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }
}
