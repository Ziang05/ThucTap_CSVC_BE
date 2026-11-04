using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class TwoDepartmentLoanTests
{
    private const string Root = "/api/v1/device-loans";

    [PostgreSqlFact]
    public async Task Two_normal_departments_complete_loan_without_equipment_configuration_and_keep_ownership()
    {
        await using var f = await Fixture.Create();
        await f.Db(async db => { (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = null; await db.SaveChangesAsync(); });
        var options = await Read(f.A, Root + "/eligible-devices");
        Assert.DoesNotContain(options.GetProperty("items").EnumerateArray(), i => i.GetProperty("deviceCode").GetString() == "OWN");
        Assert.All(options.GetProperty("items").EnumerateArray(), i => { Assert.False(i.TryGetProperty("purchasePrice", out _)); Assert.False(i.TryGetProperty("notes", out _)); });
        var code = await f.Loan();
        Assert.Equal(1, (await Read(f.B, Root)).GetProperty("totalCount").GetInt32());
        await Expect(await f.C.GetAsync(Root + "/" + code), HttpStatusCode.NotFound);
        await Expect(await f.C.PostAsJsonAsync(Root + "/" + code + "/approve", new { version = 1 }), HttpStatusCode.NotFound);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.A.PostAsJsonAsync(Root + "/" + code + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await f.Post(f.B, code, "approve", new { version = 1 });
        await f.Post(f.A, code, "handover-complete", f.Items(2));
        await f.Post(f.A, code, "return", f.Items(3, "Damaged"));
        await f.Db(async db =>
        {
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
            Assert.Equal(DeviceCondition.Good, (await db.Devices.SingleAsync(d => d.DeviceCode == "READY")).Condition);
        });
        f.Host.Clock.Advance(TimeSpan.FromHours(2));
        var pending = await Read(f.A, Root + "/" + code);
        Assert.True(pending.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("PendingReturnConfirmation", pending.GetProperty("workflowStatus").GetString());
        await Expect(await f.A.PostAsJsonAsync(Root + "/" + code + "/confirm-return", f.Items(4)), HttpStatusCode.Forbidden);
        await f.Post(f.B, code, "confirm-return", f.Items(4, "Damaged"));
        await f.Db(async db =>
        {
            var device = await db.Devices.SingleAsync(d => d.DeviceCode == "READY");
            Assert.Equal(f.LenderId, device.DepartmentId);
            Assert.Equal(f.StaffId, device.ResponsibleAccountId);
            Assert.Null(device.AreaId);
            Assert.Equal(DeviceCondition.Damaged, device.Condition);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            var loan = await db.DeviceLoans.SingleAsync();
            Assert.Equal(f.BId, loan.DecisionAccountId);
            Assert.Equal(f.AId, loan.HandoverAccountId);
            Assert.Equal(f.AId, loan.ReturnRequestedByAccountId);
            Assert.Equal(f.BId, loan.ReturnAccountId);
        });
        Assert.Empty((await Read(f.A, Root + "/eligible-devices?search=READY")).GetProperty("items").EnumerateArray());
    }

    [PostgreSqlFact]
    public async Task Return_rejection_keeps_hold_and_history_then_lender_can_receive_a_new_return()
    {
        await using var f = await Fixture.Create();
        var code = await f.OnLoan();
        await f.Post(f.A, code, "return", f.Items(3, "Good"));
        await f.Post(f.B, code, "reject-return", new { version = 4, reason = "Not physically delivered" });
        await f.Db(async db => { Assert.Single(await db.DeviceOperationHolds.ToListAsync()); Assert.Equal(DeviceLoanStatus.OnLoan, (await db.DeviceLoans.SingleAsync()).Status); });
        await f.Post(f.A, code, "return", f.Items(5, "Damaged"));
        await f.Post(f.B, code, "confirm-return", f.Items(6, "Good"));
        await f.Db(async db =>
        {
            Assert.Equal(2, await db.DeviceLoanHistories.CountAsync(h => h.ActionCode == "LOAN_RETURN_REQUESTED"));
            Assert.Single(await db.DeviceLoanHistories.Where(h => h.ActionCode == "LOAN_RETURN_REJECTED").ToListAsync());
            Assert.Equal(DeviceCondition.Damaged, (await db.DeviceLoanItems.SingleAsync()).DeclaredReturnCondition);
            Assert.Equal(DeviceCondition.Good, (await db.DeviceLoanItems.SingleAsync()).ConditionAtReturn);
        });
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/confirm-return", f.Items(6)), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Self_borrowing_mixed_sources_stale_versions_and_changed_actor_are_rejected()
    {
        await using var f = await Fixture.Create();
        await Expect(await f.A.PostAsJsonAsync(Root, f.Body("A", "OWN")), HttpStatusCode.Conflict);
        await Expect(await f.A.PostAsJsonAsync(Root, f.Body("PB_TEST", "OWN")), HttpStatusCode.Conflict);
        await Expect(await f.A.PostAsJsonAsync(Root, new { lenderDepartmentCode = "PB_TEST", purpose = "Mixed", dueAt = f.Host.Clock.GetUtcNow().AddDays(1), items = new[] { new { deviceCode = "READY" }, new { deviceCode = "OWN" } } }), HttpStatusCode.Conflict);
        var code = await f.Loan();
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/approve", new { version = 99 }), HttpStatusCode.Conflict);
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/approve", new { }), HttpStatusCode.BadRequest);
        await f.Db(async db => { (await db.UserProfiles.SingleAsync(p => p.Id == (db.Accounts.Where(a => a.Id == f.BId).Select(a => a.UserProfileId).First()))).DepartmentId = f.OtherId; await db.SaveChangesAsync(); });
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/approve", new { version = 1 }), HttpStatusCode.NotFound);
        await f.Db(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Equal(1, await db.DeviceLoanHistories.CountAsync()); });
    }

    [PostgreSqlFact]
    public async Task Service_hold_hides_entire_tree_before_paging_and_blocks_create_or_late_approval()
    {
        await using var f = await Fixture.Create();
        var pending = await f.Loan("CHILD");
        await f.Service("CHILD", approve: true);
        var page = await Read(f.A, Root + "/eligible-devices?pageSize=1&page=2");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32()); // READY, SECOND; both PARENT and CHILD excluded.
        var options = await Read(f.A, Root + "/eligible-devices?search=PARENT");
        Assert.Empty(options.GetProperty("items").EnumerateArray());
        await Expect(await f.A.PostAsJsonAsync(Root, f.Body("PB_TEST", "PARENT")), HttpStatusCode.Conflict);
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + pending + "/approve", new { version = 1 }), HttpStatusCode.Conflict);
        await f.Db(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Concurrent_lender_confirmations_and_service_approval_have_one_reservation_winner()
    {
        await using var f = await Fixture.Create();
        var loan1 = await f.Loan();
        var loan2 = await f.Loan();
        var service = await f.Service("READY", false);
        var responses = await Task.WhenAll(
            f.B.PostAsJsonAsync(Root + "/" + loan1 + "/approve", new { version = 1 }),
            f.B.PostAsJsonAsync(Root + "/" + loan2 + "/approve", new { version = 1 }),
            f.Manager.PostAsJsonAsync("/api/v1/service-orders/" + service + "/approve", new { requestId = Guid.NewGuid() }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        await f.Db(async db => Assert.Equal(1, await db.DeviceOperationHolds.CountAsync() + await db.Set<ServiceOrderDeviceHold>().CountAsync()));
    }

    [PostgreSqlTheory]
    [InlineData("LOAN_APPROVED")]
    [InlineData("LOAN_RETURNED")]
    public async Task Audit_failure_rolls_back_acquire_or_return_and_can_be_retried(string action)
    {
        await using var f = await Fixture.Create();
        var code = action == "LOAN_APPROVED" ? await f.Loan() : await f.OnLoan();
        if (action == "LOAN_RETURNED") await f.Post(f.A, code, "return", f.Items(3));
        f.Host.Failures.FailNextLoanAction = action;
        var route = action == "LOAN_APPROVED" ? "approve" : "confirm-return";
        object body = action == "LOAN_APPROVED" ? new { version = 1 } : f.Items(4, "Damaged");
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/" + route, body), HttpStatusCode.ServiceUnavailable);
        await f.Db(async db =>
        {
            Assert.Equal(action == "LOAN_APPROVED" ? 0 : 1, await db.DeviceOperationHolds.CountAsync());
            Assert.Equal(DeviceCondition.Good, (await db.Devices.SingleAsync(d => d.DeviceCode == "READY")).Condition);
        });
        await f.Post(f.B, code, route, body);
    }

    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_recovery_can_coexist_with_service_hold_and_never_removes_it(bool recovering)
    {
        await using var f = await Fixture.Create();
        await f.Service("READY", true);
        if (recovering) await f.Db(async db => { (await db.Devices.SingleAsync(d => d.DeviceCode == "READY")).DepartmentId = f.BorrowerId; await db.SaveChangesAsync(); });
        var body = new { sourceDepartmentCode = recovering ? "A" : "PB_TEST", targetDepartmentCode = recovering ? "PB_TEST" : "A", targetResponsibleAccountCode = recovering ? "TK_STAFF" : "A_STAFF", reason = "Move", items = new[] { new { deviceCode = "READY" } } };
        using var response = await (recovering ? f.A : f.Staff).PostAsJsonAsync("/api/v1/device-transfers", body);
        if (!recovering) { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); return; }
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>();
        var route = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "READY", condition = "Good" } } }), HttpStatusCode.NoContent);
        await f.Db(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.Equal(f.LenderId, (await db.Devices.SingleAsync(d => d.DeviceCode == "READY")).DepartmentId); });
    }

    [PostgreSqlFact]
    public async Task Legacy_is_preserved_and_only_actual_lender_can_explicitly_adopt_an_open_loan()
    {
        await using var f = await Fixture.Create();
        var code = await f.OnLoan();
        await f.Db(async db => { var loan = await db.DeviceLoans.SingleAsync(); loan.WorkflowVersion = 1; loan.LenderDepartmentId = null; loan.DecisionAccountId = f.ManagerId; await db.SaveChangesAsync(); });
        var legacyVersion = (await Read(f.A, Root + "/" + code)).GetProperty("version").GetInt32();
        using var blocked = await f.A.PostAsJsonAsync(Root + "/" + code + "/return", f.Items(3));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("LOAN_LEGACY_REVIEW_REQUIRED", await blocked.Content.ReadAsStringAsync());
        await Expect(await f.A.PostAsJsonAsync(Root + "/" + code + "/adopt-legacy", new { version = legacyVersion, evidence = "Reviewed physical custody" }), HttpStatusCode.Forbidden);
        await Expect(await f.B.PostAsJsonAsync(Root + "/" + code + "/adopt-legacy", new { version = legacyVersion, evidence = "Reviewed physical custody" }), HttpStatusCode.NoContent);
        await f.Db(async db =>
        {
            var history = await db.DeviceLoanHistories.SingleAsync(h => h.ActionCode == "LOAN_WORKFLOW_MIGRATED");
            Assert.Contains(f.ManagerId.ToString(), history.BeforeData!);
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
        });
        await f.Post(f.A, code, "return", f.Items(legacyVersion + 1));
        await f.Post(f.B, code, "confirm-return", f.Items(legacyVersion + 2));
    }

    [PostgreSqlFact]
    public async Task Migration_marks_existing_loans_legacy_without_inventing_a_lender()
    {
        await using var f = await Fixture.Create();
        await f.Db(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261104042826_ExportFacilitySupplierLink");
            var id = Guid.NewGuid(); var now = f.Host.Clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO device_loans(id,loan_code,borrower_account_id,borrower_department_id,purpose,due_at,creator_account_id,created_at,status,version)
                VALUES({id},'MT-LEGACY',{f.AId},{f.BorrowerId},'Legacy',{now.AddDays(1)},{f.AId},{now},'PENDING_APPROVAL',1)
                """);
            await migrator.MigrateAsync();
            var old = await db.DeviceLoans.SingleAsync();
            Assert.Equal(1, old.WorkflowVersion);
            Assert.Null(old.LenderDepartmentId);
            Assert.Null(old.DecisionAccountId);
        });
    }

    private static async Task<JsonElement> Read(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
    { using (response) Assert.True(response.StatusCode == expected, $"Expected {expected}; got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); }

    internal sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host) : IAsyncDisposable
    {
        public AccountApiTestHost Host { get; } = host;
        public HttpClient A { get; private set; } = null!;
        public HttpClient B { get; private set; } = null!;
        public HttpClient C { get; private set; } = null!;
        public HttpClient Staff { get; private set; } = null!;
        public HttpClient Manager { get; private set; } = null!;
        public Guid AId, BId, BorrowerId, LenderId, OtherId, StaffId, ManagerId;
        public static async Task<Fixture> Create()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(database.ConnectionString);
            var f = new Fixture(database, host);
            try
            {
                var seed = await host.SeedAsync();
                f.LenderId = seed.Department; f.StaffId = seed.Staff; f.ManagerId = seed.Manager;
                await f.Db(async db =>
                {
                    var a = new Department { Code = "A", Name = "Borrower" }; var c = new Department { Code = "C", Name = "Other" };
                    db.AddRange(a, c); f.BorrowerId = a.Id; f.OtherId = c.Id;
                    Account Add(string code, Guid department, char token)
                    {
                        var p = new UserProfile { FullName = code, DepartmentId = department };
                        var account = new Account { AccountCode = code, UserProfileId = p.Id, RoleId = 3, Email = code.ToLowerInvariant()+"@test.example", PasswordHash = "test-only" };
                        db.AddRange(p, account, AccountApiTestHost.Session(account.Id, new string(token,64))); return account;
                    }
                    f.AId = Add("A_STAFF", a.Id, 'A').Id; f.BId = Add("B_STAFF", seed.Department, 'B').Id; Add("C_STAFF", c.Id, 'C');
                    var category = new DeviceCategory { DisplayCode = "LOAN", Name = "Loan" }; db.Add(category);
                    Device AddDevice(string code, Guid department, Guid? parent = null)
                    {
                        var barcode = "BC"+Guid.NewGuid().ToString("N")[..30].ToUpperInvariant();
                        var d = new Device { DeviceCode = code, Barcode = barcode, Name = code, DepartmentId = department, CategoryId = category.Id, Quantity = 1, ResponsibleAccountId = seed.Staff, ParentDeviceId = parent };
                        db.AddRange(d, new DeviceIdentity { DeviceCode=code, Barcode=barcode, ReservedAt=host.Clock.GetUtcNow() }); return d;
                    }
                    AddDevice("READY",seed.Department); AddDevice("SECOND",seed.Department); AddDevice("OWN",a.Id);
                    AddDevice("CHILD",seed.Department,AddDevice("PARENT",seed.Department).Id);
                    (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = seed.Department;
                    await db.SaveChangesAsync();
                });
                HttpClient Client(string token) { var client=host.CreateClient(); client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return client; }
                f.A=Client(new string('A',64));f.B=Client(new string('B',64));f.C=Client(new string('C',64));f.Staff=Client(seed.StaffToken);f.Manager=Client(seed.ManagerToken);
                return f;
            }
            catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
        }
        public object Body(string lender = "PB_TEST", string code = "READY") => new { lenderDepartmentCode=lender, purpose="Borrow", dueAt=Host.Clock.GetUtcNow().AddHours(1), items=new[]{new{deviceCode=code}} };
        public object Items(int version, string condition="Good", string code="READY") => new {version,items=new[]{new{deviceCode=code,condition,note="Recorded condition"}}};
        public async Task<string> Loan(string code="READY")
        {
            using var response=await A.PostAsJsonAsync(Root,Body(code:code));
            Assert.True(response.StatusCode==HttpStatusCode.Created,await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("loanCode").GetString()!;
        }
        public async Task<string> OnLoan() { var code=await Loan();await Post(B,code,"approve",new{version=1});await Post(A,code,"handover-complete",Items(2));return code; }
        public Task Post(HttpClient client,string code,string action,object body)=>ExpectAwait(client.PostAsJsonAsync(Root+"/"+code+"/"+action,body));
        private static async Task ExpectAwait(Task<HttpResponseMessage> response)=>await Expect(await response,HttpStatusCode.NoContent);
        public async Task<string> Service(string device,bool approve)
        {
            using var response=await Staff.PostAsJsonAsync("/api/v1/service-orders",new{requestId=Guid.NewGuid(),orderType="Maintenance",items=new[]{new{deviceCode=device,executionMethod="OnSite",workDescription="Maintain"}}});
            Assert.True(response.StatusCode==HttpStatusCode.Created,await response.Content.ReadAsStringAsync());
            var code=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayCode").GetString()!;
            if(approve) await Expect(await Manager.PostAsJsonAsync("/api/v1/service-orders/"+code+"/approve",new{requestId=Guid.NewGuid()}),HttpStatusCode.NoContent);
            return code;
        }
        public async Task Db(Func<CsvcDbContext,Task> action) { await using var scope=Host.Services.CreateAsyncScope();await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>()); }
        public async ValueTask DisposeAsync() { A.Dispose();B.Dispose();C.Dispose();Staff.Dispose();Manager.Dispose();await Host.DisposeAsync();await database.DisposeAsync(); }
    }
}
