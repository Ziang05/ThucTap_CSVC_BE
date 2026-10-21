using CSVC.API.Authentication;
using CSVC.API.BackgroundServices;
using CSVC.UseCases.Authentication;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.AspNetCore.Authentication;
using CSVC.API.Extensions;
using CSVC.API.Middleware;
using CSVC.API.Services;
using CSVC.Infrastructure;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases;
using CSVC.UseCases.Abstractions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUseCases();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<ICurrentAccount, HttpCurrentAccount>();
builder.Services.AddScoped<IAuthenticationClientContext, HttpAuthenticationClientContext>();
builder.Services.AddOptions<CSVC.UseCases.Authentication.AuthenticationOptions>()
    .BindConfiguration(CSVC.UseCases.Authentication.AuthenticationOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHostedService<SessionExpirationWorker>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

// Explicit deployment task: never migrate the database implicitly on API startup.
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
    if (!database.Database.GetMigrations().Any())
    {
        app.Logger.LogInformation("No migrations yet. Add the first migration when implementing the domain model.");
    }
    else
    {
        await database.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations completed.");
    }

    return;
}

app.UseMiddleware<RequestContextMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "CSVC API v1"));
}

app.UseRouting();
app.UseCors(ApiServiceExtensions.FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapApplicationHealthChecks();

await app.RunAsync();

public partial class Program;
