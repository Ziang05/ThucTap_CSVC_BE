using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Departments.DTOs;
using CSVC.UseCases.Departments.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSVC.API.Tests;

public sealed class DepartmentApiTests
{
    private const string Root = "/api/v1/departments";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Mapping_preserves_department_identity_code_and_status()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<DepartmentMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();
        var target = new Department { Code = "PB_FIXED", Name = "Old", Status = EntityStatus.Locked };
        var id = target.Id;
        mapper.Map(new UpdateDepartmentRequest { Name = "  Phòng\t Nhân sự  ", ShortName = " NS ", Description = "   " }, target);
        Assert.Equal(id, target.Id);
        Assert.Equal("PB_FIXED", target.Code);
        Assert.Equal(EntityStatus.Locked, target.Status);
        Assert.Equal("Phòng Nhân sự", target.Name);
        Assert.Equal("NS", target.ShortName);
        Assert.Null(target.Description);
    }

    [PostgreSqlFact]
    public async Task Create_update_and_no_ops_preserve_code_and_write_atomic_snapshots()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var createdResponse = await manager.PostAsJsonAsync(Root, new
            { name = "  Phòng\t  Nhân sự ".Normalize(NormalizationForm.FormD), shortName = " NS ", description = " Nhân viên " });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await ReadAsync<DepartmentDetailDto>(createdResponse);
        Assert.EndsWith(created.Id.ToString(), createdResponse.Headers.Location!.ToString());
        Assert.Equal("PB000001", created.Code);
        Assert.StartsWith("PB", created.Code);
        Assert.Equal("Phòng Nhân sự", created.Name);
        Assert.Equal("NS", created.ShortName);
        Assert.Equal("Nhân viên", created.Description);
        Assert.Equal(EntityStatus.Active, created.Status);
        Assert.Equal(seed.Manager, created.Management!.CreatedBy);
        Assert.Equal(factory.Clock.GetUtcNow(), created.Management.CreatedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"{Root}/{created.Id}",
            new { name = "Phòng Nhân sự", shortName = "NS", description = "Nhân viên" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"{Root}/{created.Id}",
            new { name = "Phòng Nhân sự mới", shortName = "NSM" })).StatusCode);
        var detail = await manager.GetFromJsonAsync<DepartmentDetailDto>($"{Root}/{created.Id}", Json);
        Assert.Equal(created.Code, detail!.Code);
        Assert.Equal(created.Management.CreatedAt, detail.Management!.CreatedAt);
        Assert.Null(detail.Description);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var audit = await db.DepartmentHistories.Where(h => h.DepartmentId == created.Id).ToListAsync();
        Assert.Equal(2, audit.Count);
        var creation = Assert.Single(audit, h => h.ActionCode == "DEPARTMENT_CREATED");
        Assert.Null(creation.BeforeData);
        Assert.Equal(createdResponse.Headers.GetValues("X-Request-Id").Single(), creation.RequestId.ToString());
        Assert.Equal(seed.Manager, creation.ActorAccountId);
        var update = Assert.Single(audit, h => h.ActionCode == "DEPARTMENT_UPDATED");
        Assert.Equal("Phòng Nhân sự", JsonDocument.Parse(update.BeforeData!).RootElement.GetProperty("name").GetString());
        Assert.Equal("Phòng Nhân sự mới", JsonDocument.Parse(update.AfterData!).RootElement.GetProperty("name").GetString());
        Assert.All(audit, h => Assert.Equal(AuditOutcome.Success, h.Outcome));
    }

    [PostgreSqlFact]
    public async Task All_roles_read_filtered_catalogues_but_only_manager_sees_audit_and_impact()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        using var anonymous = factory.CreateClient();
        var active = await CreateAsync(manager, "Alpha", "XYZ");
        var locked = await CreateAsync(manager, "Beta", "B");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{locked.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Root)).StatusCode);
        foreach (var role in new short[] { 1, 2, 3 })
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                var account = await db.Accounts.SingleAsync(a => a.Id == seed.Staff);
                account.RoleId = role;
                await db.SaveChangesAsync();
            }
            var detail = await staff.GetFromJsonAsync<JsonElement>($"{Root}/{locked.Id}");
            Assert.Equal("Locked", detail.GetProperty("status").GetString());
            Assert.Equal(role == 1, detail.TryGetProperty("management", out _));
            Assert.False(detail.TryGetProperty("accounts", out _));
            Assert.Equal(role == 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await staff.GetAsync($"{Root}/{locked.Id}/history")).StatusCode);
            Assert.Equal(role == 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await staff.GetAsync($"{Root}/{locked.Id}/lock-impact")).StatusCode);
            var list = await staff.GetFromJsonAsync<JsonElement>($"{Root}?search=xyZ&status=Active&pageSize=1");
            Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
            Assert.Equal(active.Id, list.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.False(list.GetProperty("items")[0].TryGetProperty("management", out _));
        }
        foreach (var sort in new[] { "Name", "Code", "Status" })
        foreach (var direction in new[] { "Asc", "Desc" })
        {
            var list = await manager.GetFromJsonAsync<JsonElement>($"{Root}?sortBy={sort}&sortDirection={direction}");
            var values = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty(char.ToLowerInvariant(sort[0]) + sort[1..]).GetString()).ToArray();
            var expected = direction == "Asc" ? values.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : values.OrderByDescending(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, values);
        }
        var empty = await manager.GetFromJsonAsync<JsonElement>($"{Root}?search=not-found");
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var all = await manager.GetFromJsonAsync<JsonElement>(Root);
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Root}/{Guid.NewGuid()}")).StatusCode);
        await using var readScope = factory.Services.CreateAsyncScope();
        Assert.Equal(3, await readScope.ServiceProvider.GetRequiredService<CsvcDbContext>().DepartmentHistories.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Invalid_requests_permissions_duplicates_and_delete_are_audited_without_mutations()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        var created = await CreateAsync(manager, "Phòng Kế toán");
        foreach (var body in new object[] { new { name = "   " }, new { name = new string('X', 201) },
            new { name = "Valid", shortName = new string('X', 51) }, new { name = "Valid", description = new string('X', 4001) },
            new { name = "Valid", accountCode = "FORGED" }, new { name = "Valid", status = "Locked" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(Root, body)).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "sortBy=999", "sortDirection=999", "status=999" })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{Root}?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"{Root}/{created.Id}", new { name = "X", id = Guid.NewGuid() })).StatusCode);
        foreach (var role in new short[] { 2, 3 })
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                (await db.Accounts.SingleAsync(a => a.Id == seed.Staff)).RoleId = role;
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(Root, new { name = "Unauthorized" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"{Root}/{created.Id}", new { name = "Unauthorized" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsync($"{Root}/{created.Id}/lock", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"{Root}/{created.Id}/lock")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{created.Id}/lock", null)).StatusCode);
        await AssertCodeAsync(await manager.PostAsJsonAsync(Root, new { name = "  PHÒNG\t KẾ TOÁN ".Normalize(NormalizationForm.FormD) }),
            HttpStatusCode.Conflict, "DEPARTMENT_NAME_CONFLICT");
        await AssertCodeAsync(await manager.PutAsJsonAsync($"{Root}/{created.Id}", new { name = "New" }), HttpStatusCode.Conflict, "DEPARTMENT_LOCKED");
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync($"{Root}/{Guid.NewGuid()}", new { name = "Missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync($"{Root}/{Guid.NewGuid()}/lock", null)).StatusCode);
        await AssertCodeAsync(await manager.DeleteAsync($"{Root}/{created.Id}"), HttpStatusCode.Forbidden, "DEPARTMENT_DELETE_FORBIDDEN");
        await using var auditScope = factory.Services.CreateAsyncScope();
        var context = auditScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var audits = await context.DepartmentHistories.ToListAsync();
        Assert.Equal(7, audits.Count(h => h.ActionCode == "FAILED" && h.ReasonCode == "VALIDATION_FAILED"));
        Assert.Equal(8, audits.Count(h => h.ActionCode == "DENIED"));
        Assert.Single(audits, h => h.ActionCode == "DELETE_DENIED");
        Assert.All(audits.Where(h => h.Outcome != AuditOutcome.Success), h => { Assert.Null(h.BeforeData); Assert.Null(h.AfterData); });
        Assert.Equal("Phòng Kế toán", (await context.Departments.SingleAsync(d => d.Id == created.Id)).Name);
        Assert.Equal(2, await context.Departments.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Renaming_checks_all_stored_names_and_keeps_the_original_on_conflict()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var target = await CreateAsync(manager, "Phòng Một");
        var other = await CreateAsync(manager, "Phòng Hai");
        await AssertCodeAsync(await manager.PutAsJsonAsync($"{Root}/{target.Id}", new { name = " PHÒNG\t HAI " }),
            HttpStatusCode.Conflict, "DEPARTMENT_NAME_CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{other.Id}/lock", null)).StatusCode);
        await AssertCodeAsync(await manager.PutAsJsonAsync($"{Root}/{target.Id}", new { name = "phòng hai" }),
            HttpStatusCode.Conflict, "DEPARTMENT_NAME_CONFLICT");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"{Root}/{target.Id}", new { name = " " })).StatusCode);
        var result = await manager.GetFromJsonAsync<DepartmentDetailDto>($"{Root}/{target.Id}", Json);
        Assert.Equal(target.Name, result!.Name);
        Assert.Equal(target.Code, result.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.DepartmentHistories.CountAsync(h => h.DepartmentId == target.Id && h.ReasonCode == "DEPARTMENT_NAME_CONFLICT"));
        Assert.False(await db.DepartmentHistories.AnyAsync(h => h.ActionCode == "DEPARTMENT_UPDATED"));
    }

    [PostgreSqlFact]
    public async Task Locking_preserves_accounts_sessions_and_links_while_preventing_new_assignments()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        var impact = await manager.GetFromJsonAsync<DepartmentLockImpactDto>($"{Root}/{seed.Department}/lock-impact", Json);
        Assert.Equal(2, impact!.AccountCount);
        Assert.Null(impact.DeviceCount);
        Assert.Null(impact.RequestCount);
        Assert.False(impact.LinkCountsComplete);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{seed.Department}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{seed.Department}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/accounts/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/v1/accounts/me")).StatusCode);
        var request = new CreateAccountRequest { FullName = "New", Email = "new@example.com", RoleId = 3, DepartmentId = seed.Department };
        await AssertCodeAsync(await manager.PostAsJsonAsync("/api/v1/accounts", request), HttpStatusCode.BadRequest, "INVALID_DEPARTMENT");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile",
            new UpdateAccountProfileRequest { FullName = "Staff", DepartmentId = seed.Department })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{Root}/{seed.Department}/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{Root}/{seed.Department}/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/v1/accounts", request)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(3, await db.Accounts.CountAsync());
        Assert.False(await db.Accounts.AnyAsync(a => a.IsLock));
        Assert.Equal(2, await db.AuthSessions.CountAsync(s => s.IsActive));
        Assert.Equal(2, await db.DepartmentHistories.CountAsync(h => h.DepartmentId == seed.Department));
        Assert.All(await db.DepartmentHistories.ToListAsync(), h => Assert.DoesNotContain("token", h.AfterData!, StringComparison.OrdinalIgnoreCase));
    }

    [PostgreSqlFact]
    public async Task Concurrent_normalized_names_and_repeated_locks_produce_one_success_event()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var responses = await Task.WhenAll(manager.PostAsJsonAsync(Root, new { name = " Phòng\t Một " }),
            manager.PostAsJsonAsync(Root, new { name = "PHÒNG MỘT" }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var created = await ReadAsync<DepartmentDetailDto>(responses.Single(r => r.StatusCode == HttpStatusCode.Created));
        var locks = await Task.WhenAll(manager.PutAsync($"{Root}/{created.Id}/lock", null), manager.PutAsync($"{Root}/{created.Id}/lock", null));
        Assert.All(locks, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.Departments.CountAsync());
        Assert.Equal(1, await db.DepartmentHistories.CountAsync(h => h.ActionCode == "DEPARTMENT_CREATED"));
        Assert.Equal(1, await db.DepartmentHistories.CountAsync(h => h.ActionCode == "DEPARTMENT_LOCKED"));
        Assert.Equal(1, await db.DepartmentHistories.CountAsync(h => h.ReasonCode == "DEPARTMENT_NAME_CONFLICT"));
    }

    [PostgreSqlFact]
    public async Task Concurrent_account_assignment_and_department_lock_have_a_serial_result()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var target = await CreateAsync(manager, "Target");
        var assignment = manager.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest
            { FullName = "New", Email = "race@example.com", RoleId = 3, DepartmentId = target.Id });
        var locking = manager.PutAsync($"{Root}/{target.Id}/lock", null);
        await Task.WhenAll(assignment, locking);
        Assert.Equal(HttpStatusCode.NoContent, locking.Result.StatusCode);
        Assert.Contains(assignment.Result.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(EntityStatus.Locked, (await db.Departments.SingleAsync(d => d.Id == target.Id)).Status);
        Assert.Equal(assignment.Result.StatusCode == HttpStatusCode.Created ? 1 : 0, await db.Accounts.CountAsync(a => a.Email == "race@example.com"));
        Assert.Equal(1, await db.DepartmentHistories.CountAsync(h => h.DepartmentId == target.Id && h.ActionCode == "DEPARTMENT_LOCKED"));
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest
            { FullName = "Later", Email = "later@example.com", RoleId = 3, DepartmentId = target.Id })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Audit_failures_roll_back_create_update_lock_and_unlock()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var target = await CreateAsync(manager, "Before");
        foreach (var action in new[] { "DEPARTMENT_CREATED", "DEPARTMENT_UPDATED", "DEPARTMENT_LOCKED", "DEPARTMENT_UNLOCKED" })
        {
            if (action == "DEPARTMENT_UNLOCKED")
                Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{target.Id}/lock", null)).StatusCode);
            factory.Failures.FailNextDepartmentAction = action;
            var response = action switch
            {
                "DEPARTMENT_CREATED" => await manager.PostAsJsonAsync(Root, new { name = "Rollback" }),
                "DEPARTMENT_UPDATED" => await manager.PutAsJsonAsync($"{Root}/{target.Id}", new { name = "Changed" }),
                "DEPARTMENT_LOCKED" => await manager.PutAsync($"{Root}/{target.Id}/lock", null),
                _ => await manager.DeleteAsync($"{Root}/{target.Id}/lock")
            };
            await AssertCodeAsync(response, HttpStatusCode.ServiceUnavailable, "DEPARTMENT_UNAVAILABLE");
            var detail = await manager.GetFromJsonAsync<DepartmentDetailDto>($"{Root}/{target.Id}", Json);
            Assert.Equal("Before", detail!.Name);
            Assert.Equal(action == "DEPARTMENT_UNLOCKED" ? EntityStatus.Locked : EntityStatus.Active, detail.Status);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.Departments.CountAsync());
        Assert.Equal(2, await db.DepartmentHistories.CountAsync(h => h.Outcome == AuditOutcome.Success));
        Assert.Equal(4, await db.DepartmentHistories.CountAsync(h => h.ReasonCode == "DEPARTMENT_UNAVAILABLE"));
    }

    [PostgreSqlFact]
    public async Task History_is_scoped_paginated_read_only_and_failed_audit_returns_unavailable()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var target = await CreateAsync(manager, "History");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"{Root}/{target.Id}/lock", null)).StatusCode);
        var page = await manager.GetFromJsonAsync<JsonElement>($"{Root}/{target.Id}/history?pageSize=1");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("DEPARTMENT_LOCKED", page.GetProperty("items")[0].GetProperty("actionCode").GetString());
        Assert.Equal(target.Id, page.GetProperty("items")[0].GetProperty("departmentId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{Root}/{target.Id}/history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Root}/{Guid.NewGuid()}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Root}/{Guid.NewGuid()}/lock-impact")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await manager.DeleteAsync($"{Root}/{target.Id}/history")).StatusCode);
        factory.Failures.FailNextDepartmentAction = "FAILED";
        await AssertCodeAsync(await manager.PostAsJsonAsync(Root, new { name = "history" }), HttpStatusCode.ServiceUnavailable, "DEPARTMENT_UNAVAILABLE");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.DepartmentHistories.CountAsync());
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    private static HttpClient Client(AccountApiTestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<DepartmentDetailDto> CreateAsync(HttpClient client, string name, string? shortName = null)
    {
        var response = await client.PostAsJsonAsync(Root, new { name, shortName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<DepartmentDetailDto>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await ReadAsync<JsonElement>(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
