using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class AccountApiTests
{
    [PostgreSqlFact]
    public async Task Account_endpoints_enforce_permissions_project_safe_fields_and_filter_in_postgresql()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/accounts/{seed.Manager}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/accounts/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync("/api/v1/accounts/me/profile",
            new UpdateAccountProfileRequest { FullName = "Updated Staff", BirthDate = new DateOnly(2000, 1, 1), Phone = "123" })).StatusCode);
        var ownProfile = (await staff.GetFromJsonAsync<AccountDetailDto>("/api/v1/accounts/me"))!;
        Assert.Equal("Updated Staff", ownProfile.FullName);
        Assert.Equal(new DateOnly(2000, 1, 1), ownProfile.BirthDate);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync("/api/v1/accounts/me/profile",
            new UpdateAccountProfileRequest { FullName = "Staff", BirthDate = new DateOnly(2099, 1, 1) })).StatusCode);

        var request = new CreateAccountRequest
        {
            FullName = "  Nguyen   Van An ", Email = "NEW@EXAMPLE.COM", Phone = " 0123 ", RoleId = 3, DepartmentId = seed.Department
        };
        using var createdResponse = await manager.PostAsJsonAsync("/api/v1/accounts", request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<CreatedAccountDto>())!;
        Assert.EndsWith(created.Id.ToString(), createdResponse.Headers.Location!.ToString());
        var detail = (await manager.GetFromJsonAsync<AccountDetailDto>($"/api/v1/accounts/{created.Id}"))!;
        Assert.Equal("new@example.com", detail.Email);
        Assert.Equal("Nguyen Van An", detail.FullName);
        Assert.False(detail.HasValidSession);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), detail.CreatedAt);
        var list = (await manager.GetFromJsonAsync<PagedResult<AccountListItemDto>>(
            "/api/v1/accounts?search=nguyen&roleId=3&isLocked=false&sortBy=CreatedAt&sortDirection=Desc&page=1&pageSize=1"))!;
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);
        Assert.Equal(seed.Manager, list.Items[0].CreatedBy);
        Assert.Equal(0, (await manager.GetFromJsonAsync<PagedResult<AccountListItemDto>>(
            "/api/v1/accounts?search=not-found"))!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/v1/accounts?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/v1/accounts?sortBy=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"/api/v1/accounts/{created.Id}/profile",
            new { fullName = "An", email = "changed@example.com", roleId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/v1/accounts/me/profile",
            new UpdateAccountProfileRequest { FullName = "Staff", DepartmentId = seed.Department })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsync($"/api/v1/accounts/{seed.Manager}/lock", null)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IAccountPasswordHasher>();
        var stored = await context.Accounts.SingleAsync(a => a.Id == created.Id);
        Assert.NotEqual(stored.Email, stored.PasswordHash);
        Assert.True(passwordHasher.Verify(stored.PasswordHash, "new@example.com"));
        var audit = await context.AccountHistories.SingleAsync(a => a.TargetAccountId == created.Id && a.ActionCode == "ACCOUNT_CREATED");
        Assert.Equal(createdResponse.Headers.GetValues("X-Request-Id").Single(), audit.RequestId.ToString());
        Assert.DoesNotContain("password", audit.AfterData!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.AfterData!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"/api/v1/accounts/{created.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync("/api/v1/accounts", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync($"/api/v1/accounts/{created.Id}/profile",
            new UpdateAccountProfileRequest { FullName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"/api/v1/accounts/{seed.Staff}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/v1/accounts/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/accounts/{seed.Staff}/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/v1/accounts/me")).StatusCode);
        Assert.Equal(1, await context.AuthHistories.CountAsync(a => a.AccountId == seed.Staff && a.ActionCode == "SESSION_ENDED"));
    }

    [PostgreSqlFact]
    public async Task Password_changes_and_one_time_reset_revoke_sessions_and_never_return_reset_tokens()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync("/api/v1/accounts/me/password",
            new ChangePasswordRequest { CurrentPassword = "wrong", NewPassword = "NewPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/accounts/{seed.Staff}/password",
            new SetAccountPasswordRequest { NewPassword = "ManagerReset123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/v1/accounts/me")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            Assert.Empty(await context.PasswordResetRequests.ToListAsync());
        }
        using var reset1 = await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests", new { email = "staff@example.com" });
        using var reset2 = await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests", new { email = "staff@example.com" });
        using var unknown = await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests", new { email = "unknown@example.com" });
        Assert.Equal(HttpStatusCode.Accepted, reset1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, reset2.StatusCode);
        Assert.Equal(await reset1.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var messages = factory.Sender.Messages.ToArray();
        Assert.Equal(2, messages.Length);
        Assert.DoesNotContain(messages[0].Token, await reset1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/accounts/password-resets",
            new CompletePasswordResetRequest { Token = messages[0].Token, NewPassword = "RecoveryPass123!" })).StatusCode);

        var completions = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => anonymous.PostAsJsonAsync(
            "/api/v1/accounts/password-resets", new CompletePasswordResetRequest { Token = messages[1].Token, NewPassword = "RecoveryPass123!" })));
        Assert.Single(completions, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(completions, r => r.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in completions) response.Dispose();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var account = await context.Accounts.SingleAsync(a => a.Id == seed.Staff);
            Assert.True(scope.ServiceProvider.GetRequiredService<IAccountPasswordHasher>().Verify(account.PasswordHash, "RecoveryPass123!"));
            Assert.False(await context.PasswordResetRequests.AnyAsync(r => !r.IsClosed));
            Assert.Equal(1, await context.AuthHistories.CountAsync(a => a.AccountId == seed.Staff && a.ActionCode == "SESSION_ENDED"));
            Assert.Equal(1, await context.AccountHistories.CountAsync(a => a.ActionCode == "PASSWORD_RESET_COMPLETED"));
            Assert.DoesNotContain(await context.AccountHistories.ToListAsync(), a =>
                (a.BeforeData ?? "").Contains("RecoveryPass") || (a.AfterData ?? "").Contains("RecoveryPass"));
        }

        await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests", new { email = "staff@example.com" });
        var expired = factory.Sender.Messages.Last();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var open = await context.PasswordResetRequests.SingleAsync(r => !r.IsClosed);
            open.ExpiresAt = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/accounts/password-resets",
            new CompletePasswordResetRequest { Token = expired.Token, NewPassword = "ExpiredPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync("/api/v1/accounts/me/password",
            new ChangePasswordRequest { CurrentPassword = "OriginalPass123!", NewPassword = "ChangedManager123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await manager.GetAsync("/api/v1/accounts/me")).StatusCode);
        factory.Sender.IsConfigured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests",
            new { email = "staff@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await anonymous.PostAsJsonAsync("/api/v1/accounts/password-reset-requests",
            new { email = "unknown@example.com" })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Failed_audit_rolls_back_creation_and_concurrent_managers_cannot_lock_each_other_out()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        factory.Failures.FailNextAccountCreation = true;
        var create = new CreateAccountRequest { FullName = "Must roll back", Email = "rollback@example.com", RoleId = 2, DepartmentId = seed.Department };
        Assert.Equal(HttpStatusCode.InternalServerError, (await manager.PostAsJsonAsync("/api/v1/accounts", create)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            Assert.False(await context.Accounts.AnyAsync(a => a.Email == create.Email));
            Assert.False(await context.UserProfiles.AnyAsync(p => p.FullName == create.FullName));
        }
        var second = (await (await manager.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest
        {
            FullName = "Second Manager", Email = "manager2@example.com", RoleId = 1, DepartmentId = seed.Department
        })).Content.ReadFromJsonAsync<CreatedAccountDto>())!;
        var token = new string('A', 64);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            context.AuthSessions.Add(AccountApiTestHost.Session(second.Id, token));
            await context.SaveChangesAsync();
        }
        using var manager2 = Client(factory, token);
        var responses = await Task.WhenAll(manager.PutAsync($"/api/v1/accounts/{second.Id}/lock", null),
            manager2.PutAsync($"/api/v1/accounts/{seed.Manager}/lock", null));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        foreach (var response in responses) response.Dispose();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            Assert.Equal(1, await context.Accounts.CountAsync(a => a.RoleId == 1 && !a.IsLock));
        }
    }

    private static HttpClient Client(AccountApiTestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
