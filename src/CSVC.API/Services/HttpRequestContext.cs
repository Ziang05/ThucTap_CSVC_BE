using CSVC.API.Middleware;
using CSVC.UseCases.Abstractions;

namespace CSVC.API.Services;

public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public Guid RequestId => accessor.HttpContext?.Items[RequestContextMiddleware.ItemKey] is Guid requestId
        ? requestId
        : throw new InvalidOperationException("Request context is unavailable outside an HTTP request.");
}
