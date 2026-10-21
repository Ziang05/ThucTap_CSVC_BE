using CSVC.UseCases.Departments;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CSVC.API.Filters;

// Runs before ApiController's automatic 400 response; never persists submitted field values.
public sealed class DepartmentValidationAuditFilter(IDepartmentService departments) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid && (HttpMethods.IsPost(context.HttpContext.Request.Method)
            || HttpMethods.IsPut(context.HttpContext.Request.Method)))
        {
            Guid? id = Guid.TryParse(context.RouteData.Values.GetValueOrDefault("id")?.ToString(), out var parsed) ? parsed : null;
            await departments.RecordInvalidRequestAsync(id, context.HttpContext.RequestAborted);
        }
        await next();
    }
}
