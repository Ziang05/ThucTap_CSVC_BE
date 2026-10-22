using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Accounts.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class BusinessCodeApiTests
{
    [PostgreSqlFact]
    public async Task Manual_codes_are_normalized_and_automatic_codes_skip_occupied_numbers()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var manualDepartment = await CreateDepartmentAsync(manager, "Manual", " pb000001 ");
        Assert.Equal("PB000001", manualDepartment.GetProperty("code").GetString());
        var automaticDepartment = await CreateDepartmentAsync(manager, "Automatic", " \t ");
        Assert.Equal("PB000002", automaticDepartment.GetProperty("code").GetString());
        Assert.Equal("PB000003", (await CreateDepartmentAsync(manager, "Omitted")).GetProperty("code").GetString());
        Assert.Equal("PB000004", (await CreateDepartmentAsync(manager, "Empty", "")).GetProperty("code").GetString());
        var manual = await CreateAccountAsync(manager, seed.Department, "manual@example.com", " tk000001 ");
        Assert.Equal("TK000001", manual.AccountCode);
        Assert.Equal("TK000002", (await CreateAccountAsync(manager, seed.Department, "auto@example.com", " ")).AccountCode);
        Assert.Equal("TK000003", (await CreateAccountAsync(manager, seed.Department, "omitted@example.com")).AccountCode);
        Assert.Equal("TK000004", (await CreateAccountAsync(manager, seed.Department, "empty@example.com", "")).AccountCode);
        Assert.Equal("NV.QTTB/2026-01", (await CreateAccountAsync(manager, seed.Department, "format@example.com", " nv.qttb/2026-01 ")).AccountCode);
        using var nullDepartment = await manager.PostAsJsonAsync("/api/v1/departments", new { name = "Explicit null", code = (string?)null });
        Assert.Equal(HttpStatusCode.Created, nullDepartment.StatusCode);
        Assert.Equal("PB000005", (await nullDepartment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var nullAccount = await manager.PostAsJsonAsync("/api/v1/accounts", new
        { fullName = "Explicit null", email = "null@example.com", roleId = 2, departmentId = seed.Department, accountCode = (string?)null });
        Assert.Equal(HttpStatusCode.Created, nullAccount.StatusCode);
        Assert.Equal("TK000005", (await nullAccount.Content.ReadFromJsonAsync<CreatedAccountDto>())!.AccountCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT setval('account_code_seq', 999999)");
            await db.Database.ExecuteSqlRawAsync("SELECT setval('department_code_seq', 999999)");
        }
        Assert.Equal("TK1000000", (await CreateAccountAsync(manager, seed.Department, "million@example.com")).AccountCode);
        Assert.Equal("PB1000000", (await CreateDepartmentAsync(manager, "Million")).GetProperty("code").GetString());
    }

    [PostgreSqlFact]
    public async Task Renaming_preserves_ids_links_and_sessions_and_records_old_and_new_codes_once()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        for (var i = 0; i < 2; i++)
        {
            using var rename = await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Test Staff", accountCode = " nv-2026-01 " });
            Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
            using var renameDepartment = await manager.PutAsJsonAsync($"/api/v1/departments/{seed.Department}",
                new { name = "Test department", code = " pb.qttb/2026 " });
            Assert.Equal(HttpStatusCode.NoContent, renameDepartment.StatusCode);
        }
        var own = await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me");
        Assert.Equal(seed.Staff, own!.Id);
        Assert.Equal("NV-2026-01", own.AccountCode);
        Assert.Equal(seed.Department, own.DepartmentId);
        Assert.True(own.HasValidSession);
        Assert.Equal("staff@example.com", own.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/departments/{seed.Department}",
            new { name = "Test department", code = " " })).StatusCode);
        Assert.Equal("PB.QTTB/2026", (await DetailAsync(manager, "departments", seed.Department)).GetProperty("code").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.Id == seed.Staff);
        Assert.Equal(seed.Department, (await db.UserProfiles.SingleAsync(p => p.Id == account.UserProfileId)).DepartmentId);
        Assert.Equal(2, await db.AuthSessions.CountAsync(s => s.IsActive));
        var accountAudit = await db.AccountHistories.SingleAsync(h => h.ActionCode == "PROFILE_UPDATED");
        Assert.Equal("TK_STAFF", Value(accountAudit.BeforeData!, "account_code"));
        Assert.Equal("NV-2026-01", Value(accountAudit.AfterData!, "account_code"));
        Assert.Equal(seed.Manager, accountAudit.ActorAccountId);
        Assert.Contains("account_code", accountAudit.ChangedFields!);
        var departmentAudit = await db.DepartmentHistories.SingleAsync(h => h.ActionCode == "DEPARTMENT_UPDATED");
        Assert.Equal("PB_TEST", Value(departmentAudit.BeforeData!, "code"));
        Assert.Equal("PB.QTTB/2026", Value(departmentAudit.AfterData!, "code"));
    }

    [PostgreSqlFact]
    public async Task Invalid_duplicate_locked_and_unauthorized_code_changes_are_rejected()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        using var anonymous = factory.CreateClient();
        foreach (var code in new[] { "has space", "<script>", "ĐV01", new string('X', 33) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/accounts",
                new { fullName = "Invalid", email = "invalid@example.com", roleId = 2, departmentId = seed.Department, accountCode = code })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/departments", new { name = "Invalid", code })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Test Staff", accountCode = code })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"/api/v1/departments/{seed.Department}", new { name = "Test department", code })).StatusCode);
        }
        foreach (var body in new object[] { new { }, new { accountCode = "NV01" }, new { fullName = "Test Staff", accountCode = "NV01", roleId = 1 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", body)).StatusCode);
        await AssertProblemAsync(await manager.PostAsJsonAsync("/api/v1/accounts", new
        { fullName = "Duplicate", email = "duplicate@example.com", roleId = 2, departmentId = seed.Department, accountCode = " tk_manager " }),
            HttpStatusCode.Conflict, "ACCOUNT_CODE_CONFLICT");
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Must not change", accountCode = " tk_manager " }),
            HttpStatusCode.Conflict, "ACCOUNT_CODE_CONFLICT");
        await AssertProblemAsync(await manager.PostAsJsonAsync("/api/v1/departments", new { name = "Duplicate", code = " pb_test " }),
            HttpStatusCode.Conflict, "DEPARTMENT_CODE_CONFLICT");
        var other = await CreateDepartmentAsync(manager, "Other", "OTHER");
        var otherId = other.GetProperty("id").GetGuid();
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/departments/{otherId}", new { name = "Other", code = "pb_test" }),
            HttpStatusCode.Conflict, "DEPARTMENT_CODE_CONFLICT");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Test Staff", accountCode = "NEW" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Test Staff", accountCode = "NEW" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/v1/departments/{otherId}", new { name = "Other", code = "NEW" })).StatusCode);
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/accounts/{Guid.NewGuid()}/profile", new { fullName = "Test Staff", accountCode = "NEW" }), HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND");
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/departments/{Guid.NewGuid()}", new { name = "Missing", code = "NEW" }), HttpStatusCode.NotFound, "DEPARTMENT_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"/api/v1/accounts/{seed.Staff}/lock", null)).StatusCode);
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Test Staff", accountCode = "NEW" }), HttpStatusCode.Conflict, "ACCOUNT_LOCKED");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"/api/v1/departments/{otherId}/lock", null)).StatusCode);
        await AssertProblemAsync(await manager.PutAsJsonAsync($"/api/v1/departments/{otherId}", new { name = "Other", code = "NEW" }), HttpStatusCode.Conflict, "DEPARTMENT_LOCKED");
        Assert.Equal("TK_STAFF", (await manager.GetFromJsonAsync<AccountDetailDto>($"/api/v1/accounts/{seed.Staff}"))!.AccountCode);
        Assert.Equal("Test Staff", (await manager.GetFromJsonAsync<AccountDetailDto>($"/api/v1/accounts/{seed.Staff}"))!.FullName);
        Assert.Equal("OTHER", (await DetailAsync(manager, "departments", otherId)).GetProperty("code").GetString());
    }

    [PostgreSqlFact]
    public async Task Concurrent_creates_generate_distinct_codes_and_only_one_manual_duplicate_succeeds()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        var departments = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => CreateDepartmentAsync(manager, $"Concurrent {i}")));
        Assert.Equal(4, departments.Select(d => d.GetProperty("code").GetString()).Distinct().Count());
        Assert.All(departments, d => Assert.Matches(@"^PB\d{6}$", d.GetProperty("code").GetString()!));
        var accounts = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => CreateAccountAsync(manager, seed.Department, $"concurrent{i}@example.com")));
        Assert.Equal(4, accounts.Select(a => a.AccountCode).Distinct().Count());
        Assert.All(accounts, a => Assert.Matches(@"^TK\d{6}$", a.AccountCode));
        var accountResponses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => manager.PostAsJsonAsync("/api/v1/accounts",
            new { fullName = "Duplicate", email = $"race{i}@example.com", roleId = 2, departmentId = seed.Department, accountCode = "RACE" })));
        Assert.Single(accountResponses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(accountResponses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in accountResponses) response.Dispose();
        var departmentResponses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => manager.PostAsJsonAsync("/api/v1/departments", new { name = $"Race {i}", code = "RACE" })));
        Assert.Single(departmentResponses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(departmentResponses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in departmentResponses) response.Dispose();
    }

    [PostgreSqlFact]
    public async Task Audit_failure_rolls_back_both_code_changes()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        factory.Failures.FailNextAccountAction = "PROFILE_UPDATED";
        Assert.Equal(HttpStatusCode.InternalServerError, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new { fullName = "Must roll back", accountCode = "ROLLBACK" })).StatusCode);
        factory.Failures.FailNextDepartmentAction = "DEPARTMENT_UPDATED";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.PutAsJsonAsync($"/api/v1/departments/{seed.Department}", new { name = "Test department", code = "ROLLBACK" })).StatusCode);
        Assert.Equal("TK_STAFF", (await manager.GetFromJsonAsync<AccountDetailDto>($"/api/v1/accounts/{seed.Staff}"))!.AccountCode);
        Assert.Equal("Test Staff", (await manager.GetFromJsonAsync<AccountDetailDto>($"/api/v1/accounts/{seed.Staff}"))!.FullName);
        Assert.Equal("PB_TEST", (await DetailAsync(manager, "departments", seed.Department)).GetProperty("code").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.False(await db.AccountHistories.AnyAsync(h => h.ActionCode == "PROFILE_UPDATED"));
        Assert.False(await db.DepartmentHistories.AnyAsync(h => h.ActionCode == "DEPARTMENT_UPDATED"));
    }

    [PostgreSqlFact]
    public async Task Combined_updates_preserve_links_and_write_one_audit_and_the_separate_code_route_is_removed()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        var department = await CreateDepartmentAsync(manager, "Destination", "DEST");
        var departmentId = department.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/profile", new
        {
            fullName = "Nguyễn Văn Mới", accountCode = " nv-2027 ", phone = "0901234567",
            birthDate = "2000-01-15", departmentId
        })).StatusCode);
        var own = (await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!;
        Assert.Equal(seed.Staff, own.Id);
        Assert.Equal("NV-2027", own.AccountCode);
        Assert.Equal("Nguyễn Văn Mới", own.FullName);
        Assert.Equal("0901234567", own.Phone);
        Assert.Equal(new DateOnly(2000, 1, 15), own.BirthDate);
        Assert.Equal(departmentId, own.DepartmentId);
        Assert.True(own.HasValidSession);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/departments/{departmentId}",
            new { name = "Phòng ban mới", code = "dest-2027", shortName = "NEW" })).StatusCode);
        var departmentDetail = await DetailAsync(manager, "departments", departmentId);
        Assert.Equal("DEST-2027", departmentDetail.GetProperty("code").GetString());
        Assert.Equal("Phòng ban mới", departmentDetail.GetProperty("name").GetString());
        Assert.Equal("Phòng ban mới", (await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!.DepartmentName);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var audit = await db.AccountHistories.SingleAsync(h => h.ActionCode == "DEPARTMENT_CHANGED");
            Assert.Equal("TK_STAFF", Value(audit.BeforeData!, "account_code"));
            Assert.Equal("NV-2027", Value(audit.AfterData!, "account_code"));
            Assert.Equal("Test Staff", Value(audit.BeforeData!, "full_name"));
            Assert.Equal("Nguyễn Văn Mới", Value(audit.AfterData!, "full_name"));
            Assert.Contains("account_code", audit.ChangedFields!);
            Assert.Contains("full_name", audit.ChangedFields!);
            Assert.Contains("department_id", audit.ChangedFields!);
            Assert.False(await db.AccountHistories.AnyAsync(h => h.ActionCode == "PROFILE_UPDATED"));
            Assert.Equal(1, await db.DepartmentHistories.CountAsync(h => h.DepartmentId == departmentId && h.ActionCode == "DEPARTMENT_UPDATED"));
        }
        var swagger = await manager.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = swagger.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/accounts/{id}/profile", out _));
        Assert.False(paths.TryGetProperty("/api/v1/accounts/{id}/code", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/code", new { accountCode = "REMOVED" })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Optional_codes_preserve_existing_values_and_both_profile_routes_enforce_manager_permissions()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        foreach (var body in new object[] { new { fullName = "Test Staff" }, new { fullName = "Test Staff", accountCode = (string?)null },
            new { fullName = "Test Staff", accountCode = "" }, new { fullName = "Test Staff", accountCode = " \t " } })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync("/api/v1/accounts/me/profile", body)).StatusCode);
            Assert.Equal("TK_STAFF", (await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!.AccountCode);
        }
        foreach (var route in new[] { "/api/v1/accounts/me/profile", $"/api/v1/accounts/{seed.Staff}/profile" })
            await AssertProblemAsync(await staff.PutAsJsonAsync(route, new { fullName = "Forbidden", accountCode = "NEW" }),
                HttpStatusCode.Forbidden, "ACCOUNT_CODE_CHANGE_FORBIDDEN");
        Assert.Equal("Test Staff", (await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!.FullName);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync("/api/v1/accounts/me/profile", new { fullName = "Staff updated" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync("/api/v1/accounts/me/profile", new { fullName = "Test Manager", accountCode = "ADMIN-2027" })).StatusCode);
        Assert.Equal("ADMIN-2027", (await manager.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!.AccountCode);
    }

    [PostgreSqlFact]
    public async Task Database_rejects_case_and_padding_variants_even_when_bypassing_the_API()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        var profile = new UserProfile { FullName = "Direct SQL", DepartmentId = seed.Department };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var department = new NpgsqlCommand("INSERT INTO departments(id,code,name) VALUES (@id,' pb_test ','Direct SQL')", connection);
        department.Parameters.AddWithValue("id", Guid.NewGuid());
        var departmentError = await Assert.ThrowsAsync<PostgresException>(() => department.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, departmentError.SqlState);
        Assert.Equal("uq_departments_normalized_code", departmentError.ConstraintName);
        await using var account = new NpgsqlCommand("INSERT INTO accounts(id,account_code,email,role_id,user_profile_id,password_hash) VALUES (@id,' tk_manager ','direct@example.com',2,@profile,'test-hash')", connection);
        account.Parameters.AddWithValue("id", Guid.NewGuid());
        account.Parameters.AddWithValue("profile", profile.Id);
        var accountError = await Assert.ThrowsAsync<PostgresException>(() => account.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, accountError.SqlState);
        Assert.Equal("uq_accounts_normalized_code", accountError.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task Migration_preserves_legacy_codes_and_foreign_keys()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        await db.Database.GetService<IMigrator>().MigrateAsync("20261021022819_InitialFourModules");
        var departmentId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        const string legacyDepartment = "PB1234567890abcdef1234567890abcd";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO departments(id,code,name) VALUES ({departmentId},{legacyDepartment},'Legacy')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_profiles(id,department_id,full_name) VALUES ({profileId},{departmentId},'Legacy')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO accounts(id,account_code,email,role_id,user_profile_id,password_hash) VALUES ({accountId},'base_manager','legacy@example.com',1,{profileId},'legacy-hash')");
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(legacyDepartment, (await db.Departments.SingleAsync()).Code);
        var stored = await db.Accounts.SingleAsync();
        Assert.Equal(accountId, stored.Id);
        Assert.Equal("base_manager", stored.AccountCode);
        Assert.Equal(profileId, stored.UserProfileId);
        Assert.Equal(departmentId, (await db.UserProfiles.SingleAsync()).DepartmentId);
    }

    private static HttpClient Client(AccountApiTestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> CreateDepartmentAsync(HttpClient client, string name, string? code = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        if (code is not null) body["code"] = code;
        using var response = await client.PostAsJsonAsync("/api/v1/departments", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<CreatedAccountDto> CreateAccountAsync(HttpClient client, Guid departmentId, string email, string? code = null)
    {
        var body = new Dictionary<string, object?> { ["fullName"] = "Test", ["email"] = email, ["roleId"] = 2, ["departmentId"] = departmentId };
        if (code is not null) body["accountCode"] = code;
        using var response = await client.PostAsJsonAsync("/api/v1/accounts", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedAccountDto>())!;
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, string resource, Guid id)
    {
        using var response = await client.GetAsync($"/api/v1/{resource}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string? Value(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(key).GetString();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }
}
