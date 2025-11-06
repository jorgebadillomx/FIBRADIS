using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class DividendsPullJob
{
    private readonly IDividendImporterYahoo _importer;
    private readonly ILogger<DividendsPullJob> _logger;

    public DividendsPullJob(IDividendImporterYahoo importer, ILogger<DividendsPullJob> logger)
    {
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var jobRunId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting dividends:pull job {JobRunId}", jobRunId);
        try
        {
            var summary = await _importer.ImportAsync(ct).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "dividends:pull job {JobRunId} completed imported={Imported} duplicates={Duplicates} failed={Failed} elapsed={ElapsedMs}ms",
                jobRunId,
                summary.CountImported,
                summary.CountDuplicates,
                summary.CountFailed,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("dividends:pull job {JobRunId} cancelled after {ElapsedMs}ms", jobRunId, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "dividends:pull job {JobRunId} failed after {ElapsedMs}ms", jobRunId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
