using System.Diagnostics;
using System.Text.Json.Serialization;
using CSVC.API.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CSVC.API.Extensions;

public static class ApiServiceExtensions
{
    public const string FrontendCorsPolicy = "Frontend";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions["requestId"] =
                context.HttpContext.Items[RequestContextMiddleware.ItemKey];
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Request validation failed.",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["code"] = "VALIDATION_FAILED";
                problem.Extensions["requestId"] = context.HttpContext.Items[RequestContextMiddleware.ItemKey];
                problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                var result = new BadRequestObjectResult(problem);
                result.ContentTypes.Add("application/problem+json");
                return result;
            };
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "CSVC API", Version = "v1",
                Description = "Facilities management API: infrastructure and account/user management."
            });
            options.AddSecurityDefinition("Session", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "Opaque session token",
                Description = "An existing DB session token. Login/session issuance is implemented separately."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Session", document)] = []
            });
        });

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => origin == "*"
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.GetLeftPart(UriPartial.Authority) != origin))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit HTTP(S) origins without paths or trailing slashes.");
        }

        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
                    .WithExposedHeaders(RequestContextMiddleware.HeaderName);
            }
        }));

        return services;
    }
}
