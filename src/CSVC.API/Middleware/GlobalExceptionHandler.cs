using CSVC.Core.Exceptions;
using CSVC.UseCases.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Middleware;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            DomainException domain => (400, domain.Code, domain.Message),
            UseCaseException application => (application.Kind switch
            {
                FailureKind.Validation => 400,
                FailureKind.Unauthorized => 401,
                FailureKind.Unavailable => 503,
                FailureKind.NotFound => 404,
                FailureKind.Conflict => 409,
                FailureKind.Forbidden => 403,
                _ => 500
            }, application.Code, application.Message),
            DbUpdateConcurrencyException => (409, "CONCURRENCY_CONFLICT", "The data has changed. Reload and retry the operation."),
            BadHttpRequestException request => (request.StatusCode, "INVALID_REQUEST", "The HTTP request is invalid."),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled request failure. RequestId: {RequestId}",
                httpContext.Items[RequestContextMiddleware.ItemKey]);
        }

        httpContext.Response.StatusCode = status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = code,
                Detail = detail,
                Instance = httpContext.Request.Path,
                Extensions = { ["code"] = code }
            }
        });

        return true;
    }
}
