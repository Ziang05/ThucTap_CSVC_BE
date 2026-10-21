using CSVC.UseCases.Areas;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CSVC.API.Filters;

public sealed class AreaValidationAuditFilter(IAreaService areas) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid && (HttpMethods.IsPost(context.HttpContext.Request.Method)
            || HttpMethods.IsPut(context.HttpContext.Request.Method)))
        {
            Guid? id = Guid.TryParse(context.RouteData.Values.GetValueOrDefault("id")?.ToString(), out var parsed) ? parsed : null;
            await areas.RecordInvalidRequestAsync(id, context.HttpContext.RequestAborted);
        }
        await next();
    }
}
