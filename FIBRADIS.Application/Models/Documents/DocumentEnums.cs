namespace FIBRADIS.Application.Models.Documents;

public enum DocumentKind
{
    Other = 0,
    Quarterly = 1,
    HechoRelevante = 2,
    Presentation = 3,
    DistributionNotice = 4
}

public enum DocumentStatus
{
    New = 0,
    DownloadQueued = 1,
    Downloaded = 2,
    Parsed = 3,
    FactsExtracted = 4,
    Ignored = 5,
    Superseded = 6
}
