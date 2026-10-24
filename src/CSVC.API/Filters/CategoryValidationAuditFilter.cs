using CSVC.UseCases.DeviceCategories;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CSVC.API.Filters;

// Run before ApiController's automatic 400; never persist invalid submitted field values.
public sealed class CategoryValidationAuditFilter(ICategoryService categories) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid && (HttpMethods.IsPost(context.HttpContext.Request.Method)
            || HttpMethods.IsPut(context.HttpContext.Request.Method)))
        {
            var code = context.RouteData.Values.GetValueOrDefault("displayCode")?.ToString();
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName);
            var action = code is null ? "CATEGORY_CREATED"
                : actionName == "Lock" ? "CATEGORY_LOCKED" : "CATEGORY_UPDATED";
            await categories.RecordInvalidRequestAsync(code, action, context.HttpContext.RequestAborted);
        }
        await next();
    }
}
