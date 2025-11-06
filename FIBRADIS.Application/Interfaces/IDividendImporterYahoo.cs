using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface IDividendImporterYahoo
{
    Task<DividendImportSummary> ImportAsync(CancellationToken ct);
}
