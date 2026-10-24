using CSVC.UseCases.Suppliers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CSVC.API.Filters;

// Run before ApiController's automatic 400; never persist invalid submitted field values.
public sealed class SupplierValidationAuditFilter(ISupplierService suppliers) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid && (HttpMethods.IsPost(context.HttpContext.Request.Method)
            || HttpMethods.IsPut(context.HttpContext.Request.Method)))
        {
            var code = context.RouteData.Values.GetValueOrDefault("displayCode")?.ToString();
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName);
            var action = code is null ? "SUPPLIER_CREATED"
                : actionName == "Lock" ? "SUPPLIER_LOCKED" : "SUPPLIER_UPDATED";
            await suppliers.RecordInvalidRequestAsync(code, action, context.HttpContext.RequestAborted);
        }
        await next();
    }
}
