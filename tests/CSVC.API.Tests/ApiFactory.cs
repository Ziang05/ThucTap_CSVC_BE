using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace CSVC.API.Tests;

public sealed class ApiFactory(string environment = "Development", bool databaseHealthy = true)
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration is available when the minimal API reads its settings,
        // before ConfigureWebHost's service overrides run.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=csvc_tests;Username=csvc;Password=test-only;Timeout=1",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureServices(services =>
        {
            // Test-only controller: never included in the published API.
            services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "postgresql", new FixedHealthCheck(databaseHealthy), null, ["ready"]));
            });
        });
    }

    private sealed class FixedHealthCheck(bool healthy) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(healthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
    }
}
