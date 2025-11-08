using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface INewsClassifier
{
    Task<NewsClassification> ClassifyAsync(ExternalNewsArticle article, CancellationToken cancellationToken);
}
