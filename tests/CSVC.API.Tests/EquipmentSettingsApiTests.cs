using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CSVC.Core.Departments;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class EquipmentSettingsApiTests
{
    private const string Route = "/api/v1/settings/equipment-department";

    [PostgreSqlFact]
    public async Task Manager_selects_existing_department_persists_it_and_cannot_lock_it()
    {
        await using var db = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(db.ConnectionString);
        var seed = await host.SeedAsync();
        using var manager = Client(host, seed.ManagerToken);
        using var staff = Client(host, seed.StaffToken);
        var initial = await manager.GetFromJsonAsync<EquipmentSettingsDto>(Route);
        Assert.False(initial!.IsConfigured);
        Assert.Equal(0, initial.Version);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = 0 })).StatusCode);
        var saved = await manager.GetFromJsonAsync<EquipmentSettingsDto>(Route);
        Assert.True(saved!.IsConfigured);
        Assert.Equal("PB_TEST", saved.DepartmentCode);
        Assert.Empty(saved.MismatchedAccounts);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = saved.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsync($"/api/v1/departments/{seed.Department}/lock", null)).StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(seed.Department, (await context.EquipmentSettings.SingleAsync()).EquipmentDepartmentId);
        Assert.Single(await context.EquipmentSettingsHistories.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Bootstrap_reports_mismatches_and_manager_can_repair_accounts_without_matching_department()
    {
        await using var db = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(db.ConnectionString);
        var seed = await host.SeedAsync();
        using var manager = Client(host, seed.ManagerToken);
        using var staff = Client(host, seed.StaffToken);
        Guid equipmentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var department = new Department { Code = "FACILITIES", Name = "Facilities Office" };
            context.Add(department);
            await context.SaveChangesAsync();
            equipmentId = department.Id;
        }
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(Route, new { departmentCode = "FACILITIES", version = 0 })).StatusCode);
        var settings = await manager.GetFromJsonAsync<EquipmentSettingsDto>(Route);
        Assert.Equal(2, settings!.MismatchedAccounts.Count);
        var workflow = await staff.GetFromJsonAsync<EquipmentWorkflowContextDto>("/api/v1/settings/workflow-context");
        Assert.False(workflow!.EquipmentAccountMatchesDepartment);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/device-loans", new { lenderDepartmentCode = "OTHER", purpose = "Test", dueAt = host.Clock.GetUtcNow().AddDays(1), items = new[] { new { deviceCode = "TB001" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest { FullName = "Wrong", Email = "wrong@example.com", RoleId = 2, DepartmentId = seed.Department })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new UpdateAccountProfileRequest { FullName = "Test Staff", DepartmentId = equipmentId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Manager}/profile", new UpdateAccountProfileRequest { FullName = "Test Manager", DepartmentId = equipmentId })).StatusCode);
        Assert.Empty((await manager.GetFromJsonAsync<EquipmentSettingsDto>(Route))!.MismatchedAccounts);
        Assert.True((await staff.GetFromJsonAsync<EquipmentWorkflowContextDto>("/api/v1/settings/workflow-context"))!.EquipmentAccountMatchesDepartment);
        Assert.Equal(equipmentId, (await staff.GetFromJsonAsync<EquipmentWorkflowContextDto>("/api/v1/settings/workflow-context"))!.ActorDepartmentId);
    }

    [PostgreSqlFact]
    public async Task Settings_audit_failure_rolls_back_and_concurrent_saves_use_version()
    {
        await using var db = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(db.ConnectionString);
        var seed = await host.SeedAsync();
        using var manager = Client(host, seed.ManagerToken);
        host.Failures.FailNextSettingsChange = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = 0 })).StatusCode);
        Assert.False((await manager.GetFromJsonAsync<EquipmentSettingsDto>(Route))!.IsConfigured);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => manager.PutAsJsonAsync(Route, new { departmentCode = "PB_TEST", version = 0 })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().EquipmentSettingsHistories.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Missing_configuration_blocks_workflows_but_not_initial_account_setup()
    {
        await using var db = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(db.ConnectionString);
        var seed = await host.SeedAsync();
        using var manager = Client(host, seed.ManagerToken);
        using var staff = Client(host, seed.StaffToken);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/v1/device-loans", new { lenderDepartmentCode = "OTHER", purpose = "Test", dueAt = host.Clock.GetUtcNow().AddDays(1), items = new[] { new { deviceCode = "TB001" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest { FullName = "Bootstrap Staff", Email = "bootstrap@example.com", RoleId = 2, DepartmentId = seed.Department })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(Route, new { departmentCode = "UNKNOWN", version = 0 })).StatusCode);
    }

    private static HttpClient Client(AccountApiTestHost host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
