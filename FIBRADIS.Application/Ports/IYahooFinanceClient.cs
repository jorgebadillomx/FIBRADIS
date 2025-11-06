using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Services.Models;

namespace FIBRADIS.Application.Ports;

public interface IYahooFinanceClient
{
    Task<IReadOnlyList<YahooDividendEvent>> GetDividendSeriesAsync(string ticker, CancellationToken ct);
}
