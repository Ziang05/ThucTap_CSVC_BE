using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.History.Mapping;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSVC.API.Tests;

public sealed class HistoryApiTests
{
    [Fact]
    public void History_mapping_configuration_is_valid()
        => new MapperConfiguration(c => c.AddProfile<HistoryMappingProfile>(), NullLoggerFactory.Instance)
            .AssertConfigurationIsValid();

    [PostgreSqlFact]
    public async Task Lists_are_small_and_details_include_complete_audit_context_without_credentials()
    {
        await using var env = await Harness.CreateAsync();
        foreach (var domain in new[] { "accounts", "auth", "departments" })
        {
            var list = await GetAsync(env.Manager, $"/api/v1/history/{domain}");
            Assert.True(list.GetProperty("totalCount").GetInt32() > 0);
            foreach (var item in list.GetProperty("items").EnumerateArray())
            {
                foreach (var omitted in new[] { "requestId", "beforeData", "afterData", "changedFields", "sessionId", "ipAddress", "userAgent", "blockedUntil" })
                    Assert.False(item.TryGetProperty(omitted, out _), $"{domain} list contains {omitted}");
                Assert.True(item.TryGetProperty("actorAccountCode", out _));
                var actorId = item.GetProperty("actorAccountId");
                var actorName = item.GetProperty("actorFullName");
                if (actorId.ValueKind == JsonValueKind.Null)
                    Assert.Equal(JsonValueKind.Null, actorName.ValueKind);
                else
                    Assert.Equal(actorId.GetGuid() == env.ManagerId ? "Trưởng phòng Nguyễn Đình" : "Đặng Thị Hồng Ánh",
                        actorName.GetString());
                Assert.True(item.TryGetProperty("occurredAt", out _));
            }
        }
        var account = await GetAsync(env.Manager, $"/api/v1/history/accounts/{env.AccountEvents[0].Id}");
        Assert.Equal("Trưởng phòng Nguyễn Đình", account.GetProperty("actorFullName").GetString());
        Assert.Equal("Đặng Thị Hồng Ánh", account.GetProperty("targetFullName").GetString());
        Assert.Equal(env.AccountEvents[0].RequestId, account.GetProperty("requestId").GetGuid());
        AssertJsonEqual(env.AccountEvents[0].BeforeData!, account.GetProperty("beforeData"));
        AssertJsonEqual(env.AccountEvents[0].AfterData!, account.GetProperty("afterData"));
        AssertJsonEqual(env.AccountEvents[0].ChangedFields!, account.GetProperty("changedFields"));
        var auth = await GetAsync(env.Manager, $"/api/v1/history/auth/{env.AuthEvents[0].Id}");
        Assert.Equal("127.0.0.1", auth.GetProperty("ipAddress").GetString());
        Assert.Equal("Test browser/1.0", auth.GetProperty("userAgent").GetString());
        Assert.Equal(env.SessionId, auth.GetProperty("sessionId").GetGuid());
        var logout = await GetAsync(env.Manager, $"/api/v1/history/auth/{env.AuthEvents[1].Id}");
        Assert.Equal("::1", logout.GetProperty("ipAddress").GetString());
        var failed = await GetAsync(env.Manager, $"/api/v1/history/auth/{env.AuthEvents[2].Id}");
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("actorFullName").ValueKind);
        Assert.Equal("unknown@example.com", failed.GetProperty("emailSubmitted").GetString());
        var blocked = await GetAsync(env.Manager, $"/api/v1/history/auth/{env.AuthEvents[3].Id}");
        Assert.Equal(env.Time.AddMinutes(20), blocked.GetProperty("blockedUntil").GetDateTimeOffset());
        var department = await GetAsync(env.Manager, $"/api/v1/history/departments/{env.DepartmentEvents[1].Id}");
        Assert.Equal("Phòng Quản trị thiết bị", department.GetProperty("departmentName").GetString());
        AssertJsonEqual(env.DepartmentEvents[1].BeforeData!, department.GetProperty("beforeData"));
        AssertJsonEqual(env.DepartmentEvents[1].AfterData!, department.GetProperty("afterData"));
        foreach (var response in new[] { account, auth, department })
        {
            Assert.DoesNotContain("passwordHash", response.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessToken", response.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sessionTokenHash", response.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [PostgreSqlFact]
    public async Task Real_login_and_logout_are_queryable_and_logout_reason_is_recorded_once()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var anonymous = factory.CreateClient();
        using var loginResponse = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = "staff@example.com", password = "OriginalPass123!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var signedIn = Client(factory.CreateClient(), login.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        using var manager = Client(factory.CreateClient(), seed.ManagerToken);
        await AssertCountAsync(manager, "auth", $"accountId={seed.Staff}&actionCode=LOGIN_SUCCESS", 1);
        var list = await GetAsync(manager, $"/api/v1/history/auth?accountId={seed.Staff}&actionCode=SESSION_ENDED&reasonCode=logout");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var detail = await GetAsync(manager, $"/api/v1/history/auth/{id}");
        Assert.Equal("LOGOUT", detail.GetProperty("reasonCode").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.AuthHistories.CountAsync(h => h.AccountId == seed.Staff && h.ActionCode == "SESSION_ENDED"));
        Assert.Single(await db.AuthHistories.Where(h => h.AccountId == seed.Staff && h.ActionCode == "SESSION_ENDED" && h.ReasonCode == null).ToListAsync());
        using var nextLoginResponse = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = "staff@example.com", password = "OriginalPass123!" });
        Assert.Equal(HttpStatusCode.OK, nextLoginResponse.StatusCode);
        var nextLogin = await nextLoginResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var expired = Client(factory.CreateClient(), nextLogin.GetProperty("accessToken").GetString()!);
        factory.Clock.Advance(TimeSpan.FromHours(9));
        Assert.Equal(HttpStatusCode.NoContent, (await expired.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        await AssertCountAsync(manager, "auth", $"accountId={seed.Staff}&actionCode=SESSION_ENDED&reasonCode=logout", 1);
        var expiredEvent = await db.AuthHistories.AsNoTracking().SingleAsync(h => h.AccountId == seed.Staff && h.ActionCode == "SESSION_ENDED"
            && h.OccurredAt == nextLogin.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Null(expiredEvent.ReasonCode);
        Assert.Null(expiredEvent.ActorAccountId);
    }

    [PostgreSqlFact]
    public async Task All_lists_filter_count_and_sort_including_time_offsets_and_literal_search()
    {
        await using var env = await Harness.CreateAsync();
        foreach (var domain in new[] { "accounts", "auth", "departments" })
        {
            await AssertCountAsync(env.Manager, domain, "outcome=Failed", domain == "auth" ? 2 : 1);
            await AssertCountAsync(env.Manager, domain, $"actorAccountId={env.ManagerId}&from={Uri.EscapeDataString(env.Time.ToString("O"))}", domain == "auth" ? 0 : 2);
            await AssertCountAsync(env.Manager, domain, "search=not-found", 0);
            await AssertCountAsync(env.Manager, domain, "search=%25_%27", 0);
            var instant = Uri.EscapeDataString("2026-10-21T19:00:00+07:00");
            await AssertCountAsync(env.Manager, domain, $"from={instant}&to={instant}", domain == "auth" ? 2 : 1);
            foreach (var sortBy in new[] { "OccurredAt", "ActionCode", "Outcome", "ActorAccountCode" })
            foreach (var direction in new[] { "Asc", "Desc" })
            {
                var list = await GetAsync(env.Manager, $"/api/v1/history/{domain}?sortBy={sortBy}&sortDirection={direction}");
                Assert.True(list.GetProperty("items").GetArrayLength() > 0);
                if (sortBy == "OccurredAt")
                {
                    var dates = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("occurredAt").GetDateTimeOffset()).ToArray();
                    Assert.Equal(direction == "Asc" ? dates.Order().ToArray() : dates.OrderDescending().ToArray(), dates);
                }
                else if (sortBy == "Outcome")
                {
                    var outcomes = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("outcome").GetString()).ToArray();
                    Assert.Equal(direction == "Asc" ? outcomes.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                        : outcomes.OrderByDescending(x => x, StringComparer.Ordinal).ToArray(), outcomes);
                }
                else
                {
                    var key = sortBy == "ActionCode" ? "actionCode" : "actorAccountCode";
                    var values = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty(key).GetString()).ToArray();
                    var sorted = direction == "Asc"
                        ? values.OrderBy(x => x is null).ThenBy(x => x, StringComparer.Ordinal).ToArray()
                        : values.OrderByDescending(x => x is null).ThenByDescending(x => x, StringComparer.Ordinal).ToArray();
                    Assert.Equal(sorted, values);
                }
            }
        }
        await AssertCountAsync(env.Manager, "accounts", $"targetAccountId={env.StaffId}", 3);
        await AssertCountAsync(env.Manager, "accounts", "actionCode=profile_updated", 1);
        await AssertCountAsync(env.Manager, "accounts", "reasonCode=account_not_found", 1);
        await AssertCountAsync(env.Manager, "accounts", $"requestId={env.AccountEvents[0].RequestId}", 1);
        var search = Uri.EscapeDataString("TRƯỞNG PHÒNG".Normalize(NormalizationForm.FormD));
        await AssertCountAsync(env.Manager, "accounts", $"search={search}&from={Uri.EscapeDataString(env.Time.ToString("O"))}", 2);
        await AssertCountAsync(env.Manager, "auth", $"accountId={env.StaffId}", 4);
        await AssertCountAsync(env.Manager, "auth", $"sessionId={env.SessionId}", 3);
        await AssertCountAsync(env.Manager, "auth", "emailSubmitted=UNKNOWN%40EXAMPLE.COM&reasonCode=invalid_credentials", 1);
        await AssertCountAsync(env.Manager, "auth", $"requestId={env.AuthEvents[0].RequestId}", 2);
        await AssertCountAsync(env.Manager, "departments", $"departmentId={env.DepartmentId}", 2);
        await AssertCountAsync(env.Manager, "departments", "actionCode=department_updated", 1);
    }

    [PostgreSqlFact]
    public async Task Pagination_is_stable_and_department_scopes_cannot_be_overridden()
    {
        await using var env = await Harness.CreateAsync();
        foreach (var domain in new[] { "accounts", "auth", "departments" })
        {
            var all = await GetAsync(env.Manager, $"/api/v1/history/{domain}");
            var expected = all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
            var actual = new List<Guid>();
            for (var page = 1; page <= expected.Length; page++)
            {
                var current = await GetAsync(env.Manager, $"/api/v1/history/{domain}?pageSize=1&page={page}");
                Assert.Equal(expected.Length, current.GetProperty("totalCount").GetInt32());
                actual.Add(current.GetProperty("items")[0].GetProperty("id").GetGuid());
            }
            Assert.Equal(expected, actual);
            Assert.Empty((await GetAsync(env.Manager, $"/api/v1/history/{domain}?page=100")).GetProperty("items").EnumerateArray());
        }
        var scoped = $"/api/v1/departments/{env.DepartmentId}/history";
        Assert.Equal(2, (await GetAsync(env.Manager, scoped)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await GetAsync(env.Manager, $"{scoped}?departmentId={env.OtherDepartmentId}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await GetAsync(env.Manager, $"/api/v1/departments/{env.OtherDepartmentId}/history")).GetProperty("totalCount").GetInt32());
        Assert.Equal(env.DepartmentEvents[0].Id, (await GetAsync(env.Manager, $"{scoped}/{env.DepartmentEvents[0].Id}")).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.GetAsync($"/api/v1/departments/{env.OtherDepartmentId}/history/{env.DepartmentEvents[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.GetAsync($"/api/v1/departments/{Guid.NewGuid()}/history")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Only_managers_read_history_and_endpoints_are_read_only()
    {
        await using var env = await Harness.CreateAsync();
        using var anonymous = env.Factory.CreateClient();
        var paths = new[] { "/api/v1/history/accounts", "/api/v1/history/auth", "/api/v1/history/departments",
            $"/api/v1/history/accounts/{env.AccountEvents[0].Id}", $"/api/v1/history/auth/{env.AuthEvents[0].Id}",
            $"/api/v1/history/departments/{env.DepartmentEvents[0].Id}", $"/api/v1/departments/{env.DepartmentId}/history/{env.DepartmentEvents[0].Id}" };
        foreach (var path in paths) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        foreach (var role in new short[] { 2, 3 })
        {
            await using (var scope = env.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                (await db.Accounts.SingleAsync(a => a.Id == env.StaffId)).RoleId = role;
                await db.SaveChangesAsync();
            }
            foreach (var path in paths) Assert.Equal(HttpStatusCode.Forbidden, (await env.Staff.GetAsync(path)).StatusCode);
        }
        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await env.Manager.PostAsJsonAsync(path, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await env.Manager.PutAsJsonAsync(path, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await env.Manager.DeleteAsync(path)).StatusCode);
        }
        foreach (var domain in new[] { "accounts", "auth", "departments" })
            Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.GetAsync($"/api/v1/history/{domain}/{Guid.NewGuid()}")).StatusCode);
        await using var readScope = env.Factory.Services.CreateAsyncScope();
        var context = readScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(4, await context.AccountHistories.CountAsync());
        Assert.Equal(5, await context.AuthHistories.CountAsync());
        Assert.Equal(3, await context.DepartmentHistories.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Invalid_queries_are_rejected_and_swagger_distinguishes_list_and_detail_schemas()
    {
        await using var env = await Harness.CreateAsync();
        foreach (var domain in new[] { "accounts", "auth", "departments" })
        foreach (var query in new[] { "page=0", "page=1000001", "pageSize=101", "sortBy=999", "sortDirection=999", "outcome=999",
            "from=not-a-date", "from=2026-10-22T00:00:00Z&to=2026-10-21T00:00:00Z", $"actorAccountId={Guid.Empty}",
            $"requestId={Guid.Empty}", "actionCode=" + new string('X', 65), "search=" + new string('X', 201) })
            Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.GetAsync($"/api/v1/history/{domain}?{query}")).StatusCode);
        foreach (var (domain, filter) in new[] { ("accounts", "targetAccountId"), ("auth", "accountId"), ("auth", "sessionId"), ("departments", "departmentId") })
            Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.GetAsync($"/api/v1/history/{domain}?{filter}={Guid.Empty}")).StatusCode);
        var swagger = await GetAsync(env.Manager, "/swagger/v1/swagger.json");
        foreach (var domain in new[] { "accounts", "auth", "departments" })
        {
            Assert.True(swagger.GetProperty("paths").TryGetProperty($"/api/v1/history/{domain}", out _));
            Assert.True(swagger.GetProperty("paths").TryGetProperty($"/api/v1/history/{domain}/{{id}}", out _));
        }
    }

    [PostgreSqlFact]
    public async Task List_SQL_does_not_select_snapshots_or_authentication_detail_columns()
    {
        await using var env = await Harness.CreateAsync();
        var capture = new SqlCapture();
        await using var factory = env.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddDbContext<CsvcDbContext>(o => o.AddInterceptors(capture))));
        using var manager = Client(factory.CreateClient(), env.ManagerToken);
        foreach (var (domain, table) in new[] { ("accounts", "account_history"), ("auth", "auth_history"), ("departments", "department_history") })
        {
            capture.Commands.Clear();
            await GetAsync(manager, $"/api/v1/history/{domain}?pageSize=1");
            var commands = capture.Commands.Where(c => c.Contains($"FROM {table}", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(commands);
            Assert.Contains(commands, c => c.Contains("LIMIT", StringComparison.Ordinal));
            foreach (var command in commands)
            foreach (var excluded in new[] { "before_data", "after_data", "changed_fields", "user_agent", "ip_address", "session_id", "blocked_until" })
                Assert.DoesNotContain(excluded, command, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task AssertCountAsync(HttpClient client, string domain, string query, int count)
        => Assert.Equal(count, (await GetAsync(client, $"/api/v1/history/{domain}?{query}")).GetProperty("totalCount").GetInt32());

    private static void AssertJsonEqual(string expected, JsonElement actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual.GetString()!)));

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}: {text}");
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static HttpClient Client(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Harness(AccountTestDatabase database, AccountApiTestHost factory) : IAsyncDisposable
    {
        public AccountApiTestHost Factory { get; } = factory;
        public HttpClient Manager { get; private set; } = null!;
        public HttpClient Staff { get; private set; } = null!;
        public Guid ManagerId { get; private set; }
        public Guid StaffId { get; private set; }
        public Guid DepartmentId { get; private set; }
        public Guid OtherDepartmentId { get; private set; }
        public Guid SessionId { get; private set; }
        public string ManagerToken { get; private set; } = "";
        public DateTimeOffset Time { get; private set; }
        public AccountHistory[] AccountEvents { get; private set; } = [];
        public AuthHistory[] AuthEvents { get; private set; } = [];
        public DepartmentHistory[] DepartmentEvents { get; private set; } = [];

        public static async Task<Harness> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var factory = new AccountApiTestHost(database.ConnectionString);
            var env = new Harness(database, factory);
            try { await env.SeedAsync(); return env; }
            catch { await env.DisposeAsync(); throw; }
        }

        private async Task SeedAsync()
        {
            var seed = await Factory.SeedAsync();
            ManagerId = seed.Manager; StaffId = seed.Staff; DepartmentId = seed.Department; ManagerToken = seed.ManagerToken;
            Manager = Client(Factory.CreateClient(), seed.ManagerToken); Staff = Client(Factory.CreateClient(), seed.StaffToken);
            Time = Factory.Clock.GetUtcNow();
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var manager = await db.Accounts.SingleAsync(a => a.Id == ManagerId);
            var staff = await db.Accounts.SingleAsync(a => a.Id == StaffId);
            (await db.UserProfiles.SingleAsync(p => p.Id == manager.UserProfileId)).FullName = "Trưởng phòng Nguyễn Đình";
            (await db.UserProfiles.SingleAsync(p => p.Id == staff.UserProfileId)).FullName = "Đặng Thị Hồng Ánh";
            (await db.Departments.SingleAsync()).Name = "Phòng Quản trị thiết bị";
            var other = new Department { Code = "PB_OTHER", Name = "Tài chính" }; OtherDepartmentId = other.Id;
            var session = new AuthSession { AccountId = StaffId, SessionTokenHash = new string('F', 64), IsActive = false, ExpiresAt = Time.AddHours(-1) };
            SessionId = session.Id;
            db.AddRange(other, session);
            AccountEvents =
            [
                new() { RequestId = Guid.NewGuid(), ActorAccountId = ManagerId, TargetAccountId = StaffId,
                    ActionCode = "PROFILE_UPDATED", Outcome = AuditOutcome.Success, OccurredAt = Time,
                    ChangedFields = "[\"full_name\"]", BeforeData = "{\"full_name\":\"Trước\"}", AfterData = "{\"full_name\":\"Sau\"}" },
                new() { RequestId = Guid.NewGuid(), ActorAccountId = StaffId, TargetAccountId = StaffId,
                    ActionCode = "PASSWORD_CHANGED", Outcome = AuditOutcome.Success, OccurredAt = Time.AddMinutes(1) },
                new() { RequestId = Guid.NewGuid(), ActorAccountId = ManagerId, ActionCode = "FAILED", Outcome = AuditOutcome.Failed,
                    ReasonCode = "ACCOUNT_NOT_FOUND", OccurredAt = Time.AddMinutes(1) }
            ];
            var loginRequest = Guid.NewGuid();
            AuthEvents =
            [
                new() { RequestId = loginRequest, ActorAccountId = StaffId, AccountId = StaffId, SessionId = SessionId,
                    ActionCode = "LOGIN_SUCCESS", Outcome = AuditOutcome.Success, OccurredAt = Time, EmailSubmitted = "staff@example.com",
                    IpAddress = IPAddress.Loopback, UserAgent = "Test browser/1.0" },
                new() { RequestId = Guid.NewGuid(), ActorAccountId = StaffId, AccountId = StaffId, SessionId = SessionId,
                    ActionCode = "SESSION_ENDED", Outcome = AuditOutcome.Success, ReasonCode = "LOGOUT", OccurredAt = Time.AddMinutes(1), IpAddress = IPAddress.IPv6Loopback },
                new() { RequestId = Guid.NewGuid(), ActionCode = "LOGIN_FAILED", Outcome = AuditOutcome.Failed, ReasonCode = "INVALID_CREDENTIALS",
                    EmailSubmitted = "unknown@example.com", OccurredAt = Time.AddMinutes(2) },
                new() { RequestId = Guid.NewGuid(), AccountId = StaffId, ActionCode = "LOGIN_BLOCKED", Outcome = AuditOutcome.Failed,
                    ReasonCode = "LOGIN_BLOCKED", EmailSubmitted = "staff@example.com", OccurredAt = Time.AddMinutes(3), BlockedUntil = Time.AddMinutes(20) },
                new() { RequestId = loginRequest, ActorAccountId = StaffId, AccountId = StaffId, SessionId = SessionId,
                    ActionCode = "SESSION_CREATED", Outcome = AuditOutcome.Success, OccurredAt = Time }
            ];
            DepartmentEvents =
            [
                new() { RequestId = Guid.NewGuid(), ActorAccountId = ManagerId, DepartmentId = DepartmentId,
                    ActionCode = "DEPARTMENT_CREATED", Outcome = AuditOutcome.Success, OccurredAt = Time, AfterData = "{\"name\":\"Phòng cũ\"}" },
                new() { RequestId = Guid.NewGuid(), ActorAccountId = ManagerId, DepartmentId = DepartmentId,
                    ActionCode = "DEPARTMENT_UPDATED", Outcome = AuditOutcome.Success, OccurredAt = Time.AddMinutes(1),
                    BeforeData = "{\"name\":\"Phòng cũ\"}", AfterData = "{\"name\":\"Phòng mới\"}" },
                new() { RequestId = Guid.NewGuid(), ActionCode = "FAILED", Outcome = AuditOutcome.Failed,
                    ReasonCode = "VALIDATION_FAILED", OccurredAt = Time.AddMinutes(2) }
            ];
            db.AccountHistories.AddRange(AccountEvents); db.AuthHistories.AddRange(AuthEvents); db.DepartmentHistories.AddRange(DepartmentEvents);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Manager?.Dispose(); Staff?.Dispose(); await Factory.DisposeAsync(); await database.DisposeAsync();
        }
    }
}
