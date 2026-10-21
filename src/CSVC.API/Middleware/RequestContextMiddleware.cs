namespace CSVC.API.Middleware;

public sealed class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";
    public const string ItemKey = "CSVC.RequestId";

    public async Task InvokeAsync(HttpContext context)
    {
        // A fresh server ID identifies each execution; it is not an idempotency key.
        var requestId = Guid.NewGuid();
        context.Items[ItemKey] = requestId;
        // Exception handling clears response headers before writing ProblemDetails.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId.ToString();
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await next(context);
        }
    }
}
