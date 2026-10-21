using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CSVC.API.Extensions;

public static class HealthEndpointExtensions
{
    public static void MapApplicationHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteResponse
        });
    }

    private static Task WriteResponse(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
        => context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            })
        }, context.RequestAborted);
}
