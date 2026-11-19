using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CSVC.API.Tests;

public sealed class MergedWorkflowAcceptanceTests
{
    [PostgreSqlFact]
    public async Task Merged_routes_use_real_api_and_optional_browser_probe_completes()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        foreach (var path in new[] { "devices", "device-receipts", "device-transfers", "device-loans", "service-orders",
            "export-slips", "procurement-requests", "materials", "materials/balances", "materials/report", "inventory-slips" })
        {
            using var response = await f.Staff.GetAsync("/api/v1/" + path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, path + ": " + await response.Content.ReadAsStringAsync());
        }
        using var denied = await f.Department.GetAsync("/api/v1/materials/balances");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var removed = await f.Staff.GetAsync("/api/v1/work-tasks");
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);

        // Opt-in bridge for a browser running on the host. All business responses still
        // come from the real API middleware/use cases and this fixture's isolated PostgreSQL database.
        var directory = Environment.GetEnvironmentVariable("CSVC_CV01_BROWSER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var ready = Path.Combine(directory, "ready.json");
        var result = Path.Combine(directory, "result.json");
        File.Delete(ready); File.Delete(result);
        var sessions = new Dictionary<string, object>();
        foreach (var (key, client, code, roleCode) in new[]
        {
            ("manager", f.Manager, "TK_MANAGER", "QTTB_MANAGER"),
            ("staff", f.Staff, "TK_STAFF", "QTTB_STAFF"),
            ("department", f.Department, "TK_DEPARTMENT", "DEPARTMENT_STAFF")
        })
            await f.InDb(async db =>
            {
                var account = await db.Accounts.SingleAsync(a => a.AccountCode == code);
                var profile = await db.UserProfiles.SingleAsync(p => p.Id == account.UserProfileId);
                var department = await db.Departments.SingleAsync(d => d.Id == profile.DepartmentId);
                sessions[key] = new
                {
                    accessToken = client.DefaultRequestHeaders.Authorization!.Parameter,
                    expiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    user = new { account.Id, account.AccountCode, profile.FullName, account.Email, account.RoleId,
                        roleCode, roleName = roleCode, departmentId = department.Id, departmentName = department.Name }
                };
            });
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://0.0.0.0:18181");
        await using var app = builder.Build();
        app.Run(async context =>
        {
            context.Response.Headers.AccessControlAllowOrigin = context.Request.Headers.Origin;
            context.Response.Headers.AccessControlAllowHeaders = "authorization,content-type,x-request-id";
            context.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = 204; return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is { } type) request.Content.Headers.TryAddWithoutValidation("Content-Type", type);
            }
            var authorization = context.Request.Headers.Authorization.ToString();
            request.Headers.TryAddWithoutValidation("Authorization", string.IsNullOrEmpty(authorization) ? "Bearer invalid-browser-session" : authorization);
            using var response = await f.Staff.SendAsync(request);
            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } contentType) context.Response.ContentType = contentType.ToString();
            await response.Content.CopyToAsync(context.Response.Body);
        });
        await app.StartAsync();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new { apiUrl = "http://localhost:18181/api/v1", sessions }, json));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            while (!File.Exists(result)) await Task.Delay(250, timeout.Token);
            using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(result, timeout.Token));
            Assert.True(evidence.RootElement.GetProperty("passed").GetBoolean(), evidence.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "Browser validation failed.");
        }
        finally { await app.StopAsync(); }
    }
}
