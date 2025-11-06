using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Jobs;

public interface IQuotesJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
