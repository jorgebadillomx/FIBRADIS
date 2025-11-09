namespace FIBRADIS.Application.Abstractions;

public interface IReportsDiscoveryMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(int discovered, int skippedRobots, int duplicates, TimeSpan elapsed);
    void RecordFailure(TimeSpan elapsed, string error);
}

public interface IDocumentDownloadMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(long bytes, bool notModified, TimeSpan elapsed);
    void RecordDuplicate(string hash);
    void RecordIgnored(string reason);
    void RecordFailure(TimeSpan elapsed, string error);
}

public interface IDocumentParseMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(bool usedOcr, int? pages, decimal confidence, TimeSpan elapsed);
    void RecordRetry(string reason);
    void RecordFailure(TimeSpan elapsed, string error);
}

public interface IDocumentFactsPipelineMetricsCollector
{
    void RecordInvocation();
    void RecordSuccess(int score, bool requiresReview, TimeSpan elapsed);
    void RecordFailure(TimeSpan elapsed, string error);
}
