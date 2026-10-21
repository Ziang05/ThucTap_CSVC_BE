using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class ApiFoundationTests
{
    [Fact]
    public async Task System_info_has_version_and_server_generated_request_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Request-Id", "untrusted-client-value");
        using var response = await client.GetAsync("/api/v1/system/info");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CSVC API", data.GetProperty("name").GetString());
        Assert.Equal("v1", data.GetProperty("apiVersion").GetString());
        Assert.True(Guid.TryParse(response.Headers.GetValues("X-Request-Id").Single(), out _));
    }

    [Fact]
    public async Task Swagger_document_and_ui_are_available_in_development()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.Equal("CSVC API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/system/info", out _));
        using var response = await client.GetAsync("/swagger/index.html");
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/api/v1/accounts", false)]
    [InlineData("/api/v1/departments", true)]
    [InlineData("/api/v1/areas", true)]
    public async Task Protected_catalogues_keep_authorization_and_common_swagger_errors(string path, bool documentsUnavailable)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var responses = document.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        foreach (var status in new[] { "400", "401", "403", "404", "409" })
            Assert.True(responses.TryGetProperty(status, out _), $"{path} is missing documented HTTP {status}.");
        Assert.Equal(documentsUnavailable, responses.TryGetProperty("503", out _));
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Swagger_is_unavailable_in_production(string path)
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/test-probe/domain", 400, "RULE_VIOLATION")]
    [InlineData("/test-probe/failure/Validation", 400, "EXPECTED_FAILURE")]
    [InlineData("/test-probe/failure/NotFound", 404, "EXPECTED_FAILURE")]
    [InlineData("/test-probe/failure/Conflict", 409, "EXPECTED_FAILURE")]
    [InlineData("/test-probe/failure/Forbidden", 403, "EXPECTED_FAILURE")]
    [InlineData("/test-probe/unexpected", 500, "INTERNAL_ERROR")]
    public async Task Exceptions_use_problem_details_without_exposing_internal_secrets(string path, int status, string code)
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-database-password", body);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), problem.GetProperty("requestId").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Invalid_models_return_validation_details_and_request_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/test-probe/validation", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), problem.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details()
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.True(Guid.TryParse(problem.GetProperty("requestId").GetString(), out _));
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task Cors_only_allows_configured_origins(string origin, bool allowed)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/system/info");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
    }

    [Theory]
    [InlineData(true, 200)]
    [InlineData(false, 503)]
    public async Task Readiness_reports_database_state_while_liveness_remains_healthy(bool databaseHealthy, int expectedStatus)
    {
        await using var factory = new ApiFactory(databaseHealthy: databaseHealthy);
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(expectedStatus, (int)ready.StatusCode);
        var report = await ready.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("postgresql", report.GetProperty("checks")[0].GetProperty("name").GetString());
        using var live = await client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unit_of_work_is_scoped_and_uses_postgresql_persistence()
    {
        await using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.IsType<UnitOfWork>(unitOfWork);
        Assert.Same(unitOfWork, scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        await using var otherScope = factory.Services.CreateAsyncScope();
        Assert.NotSame(unitOfWork, otherScope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void Core_and_use_cases_do_not_reference_infrastructure_or_web_frameworks()
    {
        var coreReferences = typeof(Core.Exceptions.DomainException).Assembly.GetReferencedAssemblies();
        var applicationReferences = typeof(UseCases.DependencyInjection).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(coreReferences, reference => reference.Name!.StartsWith("CSVC."));
        Assert.DoesNotContain(applicationReferences, reference =>
            reference.Name is "CSVC.Infrastructure" or "CSVC.API"
            || reference.Name!.StartsWith("Microsoft.AspNetCore")
            || reference.Name.StartsWith("Microsoft.EntityFrameworkCore")
            || reference.Name.StartsWith("Npgsql"));
    }
}
