using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class DividendsReconcileJob
{
    private readonly IDistributionReconcilerService _reconcilerService;
    private readonly ILogger<DividendsReconcileJob> _logger;

    public DividendsReconcileJob(
        IDistributionReconcilerService reconcilerService,
        ILogger<DividendsReconcileJob> logger)
    {
        _reconcilerService = reconcilerService ?? throw new ArgumentNullException(nameof(reconcilerService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var jobRunId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting dividends:reconcile job {JobRunId}", jobRunId);

        try
        {
            var summary = await _reconcilerService.ReconcileAsync(ct).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "dividends:reconcile job {JobRunId} completed imported={Imported} verified={Verified} ignored={Ignored} split={Split} elapsed={ElapsedMs}ms",
                jobRunId,
                summary.ImportedCount,
                summary.VerifiedCount,
                summary.IgnoredCount,
                summary.SplitCount,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("dividends:reconcile job {JobRunId} cancelled after {ElapsedMs}ms", jobRunId, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "dividends:reconcile job {JobRunId} failed after {ElapsedMs}ms", jobRunId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
