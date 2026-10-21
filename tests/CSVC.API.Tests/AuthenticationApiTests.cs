using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Authentication;
using CSVC.UseCases.Authentication.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class AuthenticationApiTests
{
    [PostgreSqlFact]
    public async Task All_three_roles_can_login_with_safe_responses_and_atomic_audit()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var creator = Authorized(factory, seed.ManagerToken);
        using var create = await creator.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest
        {
            FullName = "Department Staff", Email = "department@example.com", RoleId = 3, DepartmentId = seed.Department
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var departmentStaff = (await create.Content.ReadFromJsonAsync<CreatedAccountDto>())!;
        using var client = factory.CreateClient();
        var users = new[]
        {
            (Id: seed.Manager, Email: "manager@example.com", Password: "OriginalPass123!", Role: "QTTB_MANAGER"),
            (Id: seed.Staff, Email: "  STAFF@EXAMPLE.COM  ", Password: "OriginalPass123!", Role: "QTTB_STAFF"),
            (Id: departmentStaff.Id, Email: "department@example.com", Password: "department@example.com", Role: "DEPARTMENT_STAFF")
        };
        var tokens = new List<string>();
        foreach (var user in users)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = user.Email, Password = user.Password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
            Assert.Equal(64, login.AccessToken.Length);
            Assert.Equal("Bearer", login.TokenType);
            Assert.Equal(factory.Clock.GetUtcNow().AddHours(8), login.ExpiresAt);
            Assert.Equal(user.Id, login.User.Id);
            Assert.Equal(user.Role, login.User.RoleCode);
            Assert.DoesNotContain(login.AccessToken, login.ToString()!);
            tokens.Add(login.AccessToken);
            using var signedIn = Authorized(factory, login.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/v1/accounts/me")).StatusCode);
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var session = await context.AuthSessions.SingleAsync(s => s.AccountId == user.Id && s.IsActive);
            Assert.Equal(SessionToken.Hash(login.AccessToken), session.SessionTokenHash);
            Assert.NotEqual(login.AccessToken, session.SessionTokenHash);
            var success = await context.AuthHistories.SingleAsync(h => h.SessionId == session.Id && h.ActionCode == "LOGIN_SUCCESS");
            Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), success.RequestId.ToString());
        }
        using var checkScope = factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var audit = JsonSerializer.Serialize(await check.AuthHistories.ToListAsync());
        foreach (var token in tokens) Assert.DoesNotContain(token, audit);
        Assert.DoesNotContain("OriginalPass123!", audit);
        Assert.Equal(3, await check.AuthHistories.CountAsync(h => h.ActionCode == "SESSION_CREATED"));
        Assert.Equal(2, await check.AuthHistories.CountAsync(h => h.ActionCode == "SESSION_ENDED"));
        using var oldClient = Authorized(factory, seed.StaffToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/v1/accounts/me")).StatusCode);
        // A stale header on the public login endpoint must not prevent re-authentication.
        Assert.Equal(HttpStatusCode.OK, (await oldClient.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Failed_attempts_block_known_and_unknown_emails_without_disclosing_identity()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        string? commonDetail = null;
        foreach (var email in new[] { "staff@example.com", "unknown@example.com" })
        {
            // Concurrent failures must not lose increments or create multiple throttle rows.
            var failures = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync(
                "/api/v1/auth/login", new LoginRequest { Email = email, Password = "incorrect" })));
            foreach (var failure in failures)
            {
                using (failure)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
                    var problem = await failure.Content.ReadFromJsonAsync<JsonElement>();
                    var detail = problem.GetProperty("detail").GetString();
                    commonDetail ??= detail;
                    Assert.Equal(commonDetail, detail);
                    Assert.Equal("INVALID_CREDENTIALS", problem.GetProperty("code").GetString());
                }
            }
            using var blocked = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest { Email = email, Password = "OriginalPass123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
            Assert.Equal(commonDetail, (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var throttle = await context.AuthLoginThrottles.SingleAsync(t => t.EmailKey == email);
            Assert.Equal(5, throttle.FailedCount);
            Assert.Equal(factory.Clock.GetUtcNow().AddMinutes(15), throttle.BlockedUntil);
            Assert.Equal(1, await context.AuthHistories.CountAsync(h => h.EmailSubmitted == email && h.ActionCode == "LOGIN_TEMP_BLOCKED"));
            Assert.Equal(6, await context.AuthHistories.CountAsync(h => h.EmailSubmitted == email && h.ActionCode == "LOGIN_FAILED"));
            if (email.StartsWith("unknown", StringComparison.Ordinal))
                Assert.False(await context.AuthHistories.AnyAsync(h => h.EmailSubmitted == email && h.AccountId != null));
            Assert.False((await context.Accounts.SingleAsync(a => a.Id == seed.Staff)).IsLock);
        }
        factory.Clock.Advance(TimeSpan.FromMinutes(15));
        using var accepted = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var throttle = await context.AuthLoginThrottles.SingleAsync(t => t.EmailKey == "staff@example.com");
            Assert.Equal(0, throttle.FailedCount);
            Assert.Null(throttle.BlockedUntil);
            Assert.Null(throttle.FirstFailedAt);
        }
        using var manager = Authorized(factory, seed.ManagerToken);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync($"/api/v1/accounts/{seed.Staff}/lock", null)).StatusCode);
        using var locked = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(commonDetail, (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        using var finalScope = factory.Services.CreateScope();
        Assert.True((await finalScope.ServiceProvider.GetRequiredService<CsvcDbContext>().Accounts.SingleAsync(a => a.Id == seed.Staff)).IsLock);
    }

    [PostgreSqlFact]
    public async Task Failure_window_resets_at_ten_minutes_and_success_clears_the_chain()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest { Email = "staff@example.com", Password = "incorrect" })).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "incorrect" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var throttle = await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().AuthLoginThrottles.SingleAsync();
            Assert.Equal(1, throttle.FailedCount);
            Assert.Equal(factory.Clock.GetUtcNow(), throttle.FirstFailedAt);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" })).StatusCode);
        using var checkScope = factory.Services.CreateScope();
        Assert.Equal(0, (await checkScope.ServiceProvider.GetRequiredService<CsvcDbContext>().AuthLoginThrottles.SingleAsync()).FailedCount);
    }

    [PostgreSqlFact]
    public async Task Concurrent_logins_replace_old_sessions_and_repeated_logout_records_one_end()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        var logins = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" })));
        var tokens = new List<string>();
        foreach (var response in logins)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                tokens.Add((await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            }
        }
        string activeToken;
        Guid sessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var active = await context.AuthSessions.SingleAsync(s => s.AccountId == seed.Staff && s.IsActive);
            activeToken = tokens.Single(t => SessionToken.Hash(t) == active.SessionTokenHash);
            sessionId = active.Id;
            Assert.Equal(2, await context.AuthHistories.CountAsync(h => h.AccountId == seed.Staff && h.ActionCode == "SESSION_ENDED"));
        }
        using var signedIn = Authorized(factory, activeToken);
        using var replaced = Authorized(factory, tokens.Single(t => t != activeToken));
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/v1/accounts/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replaced.GetAsync("/api/v1/accounts/me")).StatusCode);
        var logouts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => signedIn.PostAsync("/api/v1/auth/logout", null)));
        foreach (var response in logouts) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); response.Dispose(); }
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/v1/accounts/me")).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        var final = finalScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(1, await final.AuthHistories.CountAsync(h => h.SessionId == sessionId && h.ActionCode == "SESSION_ENDED"));
        Assert.Equal(factory.Clock.GetUtcNow(), (await final.AuthHistories.SingleAsync(h => h.SessionId == sessionId && h.ActionCode == "SESSION_ENDED")).OccurredAt);
        Assert.True(await final.AuthSessions.AnyAsync(s => s.AccountId == seed.Manager && s.IsActive));
    }

    [PostgreSqlFact]
    public async Task Expiration_on_access_and_background_cleanup_preserve_the_first_end_timestamp()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        Guid sessionId;
        var expiresAt = factory.Clock.GetUtcNow().AddMinutes(1);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var session = await context.AuthSessions.SingleAsync(s => s.AccountId == seed.Staff);
            sessionId = session.Id;
            session.ExpiresAt = expiresAt;
            await context.SaveChangesAsync();
        }
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        using var staff = Authorized(factory, seed.StaffToken);
        var request = staff.GetAsync("/api/v1/accounts/me");
        var cleanup = ExpireAsync(factory);
        await Task.WhenAll(request, cleanup);
        Assert.Equal(HttpStatusCode.Unauthorized, (await request).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(0, await ExpireAsync(factory));
        using var checkScope = factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var end = await check.AuthHistories.SingleAsync(h => h.SessionId == sessionId && h.ActionCode == "SESSION_ENDED");
        Assert.Equal(expiresAt, end.OccurredAt);
        Assert.Null(end.ReasonCode);
        Assert.Null(end.ActorAccountId);
        Assert.False((await check.AuthSessions.SingleAsync(s => s.Id == sessionId)).IsActive);
    }

    [PostgreSqlFact]
    public async Task Audit_storage_failure_never_issues_a_session_and_rolls_back_old_session_revocation()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        factory.Failures.FailNextSessionCreation = true;
        using var unavailable = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "OriginalPass123!" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("accessToken", await unavailable.Content.ReadAsStringAsync());
        using var oldSession = Authorized(factory, seed.StaffToken);
        Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/v1/accounts/me")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            Assert.Equal(1, await context.AuthSessions.CountAsync(s => s.AccountId == seed.Staff));
            Assert.False(await context.AuthHistories.AnyAsync(h => h.AccountId == seed.Staff));
            Assert.Empty(await context.AuthLoginThrottles.ToListAsync());
        }
        factory.Failures.FailNextLoginFailure = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = "staff@example.com", Password = "incorrect" })).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<CsvcDbContext>().AuthLoginThrottles.ToListAsync());
    }

    [Fact]
    public async Task Login_request_validation_rejects_unexpected_fields_without_echoing_passwords()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "staff@example.com", password = "sensitive-test-password", roleId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("sensitive-test-password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Swagger_marks_login_public_and_logout_as_optional_bearer_authentication()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = document.GetProperty("paths");
        Assert.Empty(paths.GetProperty("/api/v1/auth/login").GetProperty("post").GetProperty("security").EnumerateArray());
        var logout = paths.GetProperty("/api/v1/auth/logout").GetProperty("post").GetProperty("security");
        Assert.Equal(2, logout.GetArrayLength());
        Assert.True(logout[0].TryGetProperty("Session", out _));
        Assert.Empty(logout[1].EnumerateObject());
    }

    private static HttpClient Authorized(AccountApiTestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> ExpireAsync(AccountApiTestHost factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionExpirationService>().ExpireAsync(100);
    }
}
