using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CSVC.API.Extensions;

public sealed class AuthenticationSecurityDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        // These operations must remain usable before a session exists.
        foreach (var path in new[] { "/api/v1/auth/login", "/api/v1/accounts/password-reset-requests", "/api/v1/accounts/password-resets" })
        {
            if (!document.Paths.TryGetValue(path, out var item) || item.Operations is null) continue;
            foreach (var operation in item.Operations.Values) operation.Security = [];
        }
        if (!document.Paths.TryGetValue("/api/v1/auth/logout", out var logout) || logout.Operations is null) return;
        foreach (var operation in logout.Operations.Values)
        {
            // An authorized Swagger client sends its token; anonymous/replayed logout is also valid.
            operation.Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Session", document)] = [] },
                new OpenApiSecurityRequirement()
            ];
        }
    }
}
