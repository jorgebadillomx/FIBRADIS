namespace FIBRADIS.Application.Ports;

public interface ISecurityCatalog
{
    Task<decimal?> GetLastPriceAsync(string ticker, CancellationToken ct);

    Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        throw new NotSupportedException("Batch price retrieval is not supported.");
    }
}
