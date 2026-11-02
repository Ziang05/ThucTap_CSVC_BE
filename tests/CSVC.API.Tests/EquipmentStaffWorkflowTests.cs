using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class EquipmentStaffWorkflowTests
{
    private const string Loans = "/api/v1/device-loans";
    private const string Transfers = "/api/v1/device-transfers";

    [PostgreSqlFact]
    public async Task Equipment_staff_can_create_edit_and_cancel_own_loans_and_transfers()
    {
        await using var f = await Fixture.CreateAsync();
        var loan = await f.CreateLoan(f.Staff);
        Assert.Equal("TK_STAFF", loan.GetProperty("borrowerAccountCode").GetString());
        Assert.Equal("PB_TEST", loan.GetProperty("departmentCode").GetString());
        var loanRoute = Loans + "/" + loan.GetProperty("loanCode").GetString();
        await Expect(await f.Staff.PutAsJsonAsync(loanRoute, f.LoanBody()), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsync(loanRoute + "/cancel", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsync(loanRoute + "/cancel", null), HttpStatusCode.NoContent);
        var transfer = await f.CreateTransfer();
        var transferRoute = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PutAsJsonAsync(transferRoute, f.TransferBody()), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsync(transferRoute + "/cancel", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsync(transferRoute + "/cancel", null), HttpStatusCode.NoContent);
        await f.InDb(async db =>
        {
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Equal(3, await db.DeviceLoanHistories.CountAsync());
            Assert.Equal(3, await db.DeviceTransferHistories.CountAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Equipment_staff_retains_global_management_and_can_receive_and_return_only_for_own_department()
    {
        await using var f = await Fixture.CreateAsync();
        var ownLoan = await f.CreateLoan(f.Staff);
        var ownRoute = Loans + "/" + ownLoan.GetProperty("loanCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(ownRoute + "/approve", new { }), HttpStatusCode.NoContent);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(ownRoute + "/handover-complete", f.Conditions()), HttpStatusCode.NotFound);
        await Expect(await f.Department.PostAsJsonAsync(ownRoute + "/handover-complete", f.Conditions()), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(ownRoute + "/handover-complete", f.Conditions()), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(ownRoute + "/return", f.Conditions()), HttpStatusCode.NoContent);
        var returned = await Get(f.Staff, ownRoute);
        Assert.Equal("Returned", returned.GetProperty("status").GetString());

        var otherLoan = await f.CreateLoan(f.Department, "TB-ROLE-002");
        var otherRoute = Loans + "/" + otherLoan.GetProperty("loanCode").GetString();
        var list = await Get(f.Staff, Loans);
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
        await Get(f.Staff, otherRoute);
        var history = await Get(f.Staff, otherRoute + "/history");
        Assert.Equal(JsonValueKind.String, history.GetProperty("items")[0].GetProperty("afterData").ValueKind);
        await Expect(await f.Staff.PutAsJsonAsync(otherRoute, f.LoanBody("TB-ROLE-002")), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsync(otherRoute + "/cancel", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(otherRoute + "/approve", new { }), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(otherRoute + "/handover-complete", f.Conditions("TB-ROLE-002")), HttpStatusCode.NotFound);
        await Expect(await f.Department.PostAsJsonAsync(otherRoute + "/handover-complete", f.Conditions("TB-ROLE-002")), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(otherRoute + "/return", f.Conditions("TB-ROLE-002")), HttpStatusCode.NotFound);
        await Expect(await f.Department.PostAsJsonAsync(otherRoute + "/return", f.Conditions("TB-ROLE-002")), HttpStatusCode.NoContent);
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Both_staff_roles_can_participate_on_either_side_without_bypassing_department_or_account_checks(
        bool equipmentIsSource, bool completedByEquipment)
    {
        await using var f = await Fixture.CreateAsync();
        if (!equipmentIsSource)
            await f.InDb(async db =>
            {
                var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001");
                device.DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TARGET")).Id;
                await db.SaveChangesAsync();
            });
        var transfer = await f.CreateTransfer(equipmentIsSource);
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.NoContent);
        var source = equipmentIsSource ? "TK_STAFF" : "TK_DEPARTMENT";
        var target = equipmentIsSource ? "TK_DEPARTMENT" : "TK_STAFF";
        object Body(string sourceCode, string targetCode) => new { sourceHandoverAccountCode = sourceCode, targetHandoverAccountCode = targetCode,
            items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } };
        await Get(f.ForeignStaff, route); // Global QTTB read is retained.
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/handover-complete", Body(source, target)), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete",
            equipmentIsSource ? Body("TK_MANAGER", target) : Body(source, "TK_MANAGER")), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete",
            equipmentIsSource ? Body("TK_DEPARTMENT", target) : Body(source, "TK_DEPARTMENT")), HttpStatusCode.BadRequest);
        await f.InDb(async db =>
        {
            (await db.Accounts.SingleAsync(x => x.AccountCode == "TK_DEPARTMENT")).IsLock = true;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", Body(source, target)), HttpStatusCode.BadRequest);
        await f.InDb(async db =>
        {
            (await db.Accounts.SingleAsync(x => x.AccountCode == "TK_DEPARTMENT")).IsLock = false;
            await db.SaveChangesAsync();
        });
        await Expect(await (completedByEquipment ? f.Staff : f.Department).PostAsJsonAsync(route + "/handover-complete", Body(source, target)), HttpStatusCode.NoContent);
        Assert.Equal("Completed", (await Get(f.Staff, route)).GetProperty("status").GetString());
        await f.InDb(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001");
            Assert.Equal((await db.Departments.SingleAsync(x => x.Code == (equipmentIsSource ? "PB_TARGET" : "PB_TEST"))).Id, device.DepartmentId);
            Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountCode == target)).Id, device.ResponsibleAccountId);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            var audit = await db.DeviceTransferHistories.SingleAsync(x => x.ActionCode == "TRANSFER_COMPLETED");
            Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountCode == (completedByEquipment ? "TK_STAFF" : "TK_DEPARTMENT"))).Id, audit.ActorAccountId);
        });
    }

    [PostgreSqlFact]
    public async Task New_department_actions_do_not_extend_to_managers_or_unrelated_departments()
    {
        await using var f = await Fixture.CreateAsync();
        await Expect(await f.Manager.PostAsJsonAsync(Loans, f.LoanBody()), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Forbidden);
        var transfer = await f.CreateTransfer();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.NoContent);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/handover-complete", new
        {
            sourceHandoverAccountCode = "TK_STAFF", targetHandoverAccountCode = "TK_DEPARTMENT",
            items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } }
        }), HttpStatusCode.Forbidden);
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response) Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonElement> Get(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, HttpClient manager, HttpClient staff,
        HttpClient department, HttpClient foreignStaff) : IAsyncDisposable
    {
        public HttpClient Manager { get; } = manager;
        public HttpClient Staff { get; } = staff;
        public HttpClient Department { get; } = department;
        public HttpClient ForeignStaff { get; } = foreignStaff;
        public static async Task<Fixture> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(database.ConnectionString);
            try
            {
                var seed = await host.SeedAsync();
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                var target = new Department { Code = "PB_TARGET", Name = "Target department" };
                var foreign = new Department { Code = "PB_FOREIGN", Name = "Unrelated department" };
                var profile = new UserProfile { FullName = "Department staff", DepartmentId = target.Id };
                var foreignProfile = new UserProfile { FullName = "Other equipment staff", DepartmentId = foreign.Id };
                var account = new Account { AccountCode = "TK_DEPARTMENT", Email = "department@example.com", RoleId = 3, UserProfileId = profile.Id, PasswordHash = "test-only" };
                var foreignAccount = new Account { AccountCode = "TK_FOREIGN", Email = "foreign@example.com", RoleId = 2, UserProfileId = foreignProfile.Id, PasswordHash = "test-only" };
                db.AddRange(target, foreign, profile, foreignProfile, account, foreignAccount,
                    AccountApiTestHost.Session(account.Id, new string('B', 64)), AccountApiTestHost.Session(foreignAccount.Id, new string('C', 64)));
                var category = new DeviceCategory { DisplayCode = "CAT_ROLE", Name = "Role test" };
                db.Add(category);
                foreach (var number in Enumerable.Range(1, 2))
                {
                    var code = "TB-ROLE-00" + number;
                    var barcode = "BC" + new string((char)('D' + number), 30);
                    db.Add(new DeviceIdentity { DeviceCode = code, Barcode = barcode, ReservedAt = host.Clock.GetUtcNow() });
                    db.Add(new Device { DeviceCode = code, Barcode = barcode, Name = "Device " + number, CategoryId = category.Id,
                        DepartmentId = seed.Department, ResponsibleAccountId = seed.Staff, Quantity = 1 });
                }
                await db.SaveChangesAsync();
                HttpClient Client(string token)
                {
                    var client = host.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    return client;
                }
                return new(database, host, Client(seed.ManagerToken), Client(seed.StaffToken), Client(new string('B', 64)), Client(new string('C', 64)));
            }
            catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
        }
        public object LoanBody(string deviceCode = "TB-ROLE-001") => new
        {
            purpose = "Loan for own department", dueAt = host.Clock.GetUtcNow().AddDays(7), items = new[] { new { deviceCode } }
        };
        public object Conditions(string deviceCode = "TB-ROLE-001") => new { items = new[] { new { deviceCode, condition = "Good" } } };
        public object TransferBody(bool equipmentIsSource = true) => new
        {
            sourceDepartmentCode = equipmentIsSource ? "PB_TEST" : "PB_TARGET", targetDepartmentCode = equipmentIsSource ? "PB_TARGET" : "PB_TEST",
            targetResponsibleAccountCode = equipmentIsSource ? "TK_DEPARTMENT" : "TK_STAFF", reason = "Transfer between departments",
            items = new[] { new { deviceCode = "TB-ROLE-001" } }
        };
        public async Task<JsonElement> CreateLoan(HttpClient actor, string code = "TB-ROLE-001")
        {
            using var response = await actor.PostAsJsonAsync(Loans, LoanBody(code));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async Task<JsonElement> CreateTransfer(bool equipmentIsSource = true)
        {
            using var response = await Staff.PostAsJsonAsync(Transfers, TransferBody(equipmentIsSource));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async Task InDb(Func<CsvcDbContext, Task> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>());
        }
        public async ValueTask DisposeAsync()
        {
            Manager.Dispose(); Staff.Dispose(); Department.Dispose(); ForeignStaff.Dispose();
            await host.DisposeAsync(); await database.DisposeAsync();
        }
    }
}
