using System.Diagnostics.Metrics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Infrastructure.Observability.Metrics;

namespace FIBRADIS.Api.Monitoring;

public sealed class DocumentPipelineMetricsCollector :
    IReportsDiscoveryMetricsCollector,
    IDocumentDownloadMetricsCollector,
    IDocumentParseMetricsCollector,
    IDocumentFactsPipelineMetricsCollector,
    IDisposable
{
    private const string ReportsQueue = ReportsJob.QueueName;
    private const string DownloadQueue = DownloadJob.QueueName;
    private const string ParseQueue = ParseJob.QueueName;
    private const string FactsQueue = FactsJob.QueueName;

    private readonly ObservabilityMetricsRegistry _registry;
    private readonly Meter _meter;
    private readonly Counter<long> _reportsDiscovered;
    private readonly Counter<long> _reportsRobotsSkipped;
    private readonly Counter<long> _reportsDuplicates;
    private readonly Counter<long> _downloadBytes;
    private readonly Counter<long> _downloadDuplicates;
    private readonly Counter<long> _downloadIgnored;
    private readonly Histogram<double> _downloadDuration;
    private readonly Histogram<double> _parseDuration;
    private readonly Histogram<double> _factsDuration;
    private readonly Counter<long> _factsScore;

    public DocumentPipelineMetricsCollector(ObservabilityMetricsRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _meter = new Meter("FIBRADIS.DocumentPipeline", "1.0.0");
        _reportsDiscovered = _meter.CreateCounter<long>("reports_discovered_total");
        _reportsRobotsSkipped = _meter.CreateCounter<long>("reports_skipped_robots_total");
        _reportsDuplicates = _meter.CreateCounter<long>("reports_duplicates_total");
        _downloadBytes = _meter.CreateCounter<long>("download_bytes_total", unit: "bytes");
        _downloadDuplicates = _meter.CreateCounter<long>("download_duplicates_total");
        _downloadIgnored = _meter.CreateCounter<long>("download_ignored_total");
        _downloadDuration = _meter.CreateHistogram<double>("download_duration_seconds", unit: "s");
        _parseDuration = _meter.CreateHistogram<double>("parse_duration_seconds", unit: "s");
        _factsDuration = _meter.CreateHistogram<double>("facts_duration_seconds", unit: "s");
        _factsScore = _meter.CreateCounter<long>("facts_score_total", unit: "score");
    }

    public void RecordInvocation()
    {
        // No-op; invocation counts are tracked via job counters when success/failure is recorded.
    }

    public void RecordSuccess(int discovered, int skippedRobots, int duplicates, TimeSpan elapsed)
    {
        _reportsDiscovered.Add(discovered);
        _reportsRobotsSkipped.Add(skippedRobots);
        _reportsDuplicates.Add(duplicates);
        _registry.RecordJobDuration(ReportsQueue, elapsed);
        _registry.RecordJobResult(ReportsQueue, true);
    }

    public void RecordFailure(TimeSpan elapsed, string error)
    {
        _registry.RecordJobDuration(ReportsQueue, elapsed);
        _registry.RecordJobResult(ReportsQueue, false);
    }

    void IDocumentDownloadMetricsCollector.RecordInvocation()
    {
        // No-op
    }

    public void RecordSuccess(long bytes, bool notModified, TimeSpan elapsed)
    {
        _downloadBytes.Add(bytes);
        _downloadDuration.Record(elapsed.TotalSeconds);
        _registry.RecordJobDuration(DownloadQueue, elapsed);
        _registry.RecordJobResult(DownloadQueue, true);
    }

    public void RecordDuplicate(string hash)
    {
        _downloadDuplicates.Add(1);
    }

    public void RecordIgnored(string reason)
    {
        _downloadIgnored.Add(1);
    }

    void IDocumentDownloadMetricsCollector.RecordFailure(TimeSpan elapsed, string error)
    {
        _registry.RecordJobDuration(DownloadQueue, elapsed);
        _registry.RecordJobResult(DownloadQueue, false);
    }

    void IDocumentParseMetricsCollector.RecordInvocation()
    {
        // No-op
    }

    public void RecordSuccess(bool usedOcr, int? pages, decimal confidence, TimeSpan elapsed)
    {
        _parseDuration.Record(elapsed.TotalSeconds);
        _registry.RecordJobDuration(ParseQueue, elapsed);
        _registry.RecordJobResult(ParseQueue, true);
    }

    public void RecordRetry(string reason)
    {
        // retries tracked by Hangfire
    }

    void IDocumentParseMetricsCollector.RecordFailure(TimeSpan elapsed, string error)
    {
        _parseDuration.Record(elapsed.TotalSeconds);
        _registry.RecordJobDuration(ParseQueue, elapsed);
        _registry.RecordJobResult(ParseQueue, false);
    }

    void IDocumentFactsPipelineMetricsCollector.RecordInvocation()
    {
        // No-op
    }

    public void RecordSuccess(int score, bool requiresReview, TimeSpan elapsed)
    {
        _factsDuration.Record(elapsed.TotalSeconds);
        _factsScore.Add(score);
        _registry.RecordJobDuration(FactsQueue, elapsed);
        _registry.RecordJobResult(FactsQueue, true);
    }

    void IDocumentFactsPipelineMetricsCollector.RecordFailure(TimeSpan elapsed, string error)
    {
        _factsDuration.Record(elapsed.TotalSeconds);
        _registry.RecordJobDuration(FactsQueue, elapsed);
        _registry.RecordJobResult(FactsQueue, false);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
