using FIBRADIS.Api.Middleware;
using FIBRADIS.Application.Abstractions;

namespace FIBRADIS.Api.Infrastructure;

public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CorrelationId =>
        _httpContextAccessor.HttpContext?.Features.Get<RequestTrackingFeature>()?.RequestId;
}
