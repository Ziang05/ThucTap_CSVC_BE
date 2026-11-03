using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class DeviceEligibilityTests
{
    [PostgreSqlFact]
    public async Task Eligibility_filters_whole_subtrees_before_search_paging_and_department_scope()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(database.ConnectionString);
        var seed = await host.SeedAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var category = new DeviceCategory { DisplayCode = "ELIGIBLE", Name = "Eligibility" };
            var other = new Department { Code = "OTHER", Name = "Other department" };
            var profile = new UserProfile { FullName = "Department staff", DepartmentId = seed.Department };
            var account = new Account { AccountCode = "EL_STAFF", Email = "eligible@example.test", RoleId = 3,
                UserProfileId = profile.Id, PasswordHash = "test-only" };
            db.AddRange(category, other, profile, account, AccountApiTestHost.Session(account.Id, new string('A', 64)));
            Device Add(string code, Device? parent = null, Guid? department = null)
            {
                var barcode = "BC" + Guid.NewGuid().ToString("N")[..30].ToUpperInvariant();
                db.Add(new DeviceIdentity { DeviceCode = code, Barcode = barcode, ReservedAt = host.Clock.GetUtcNow() });
                var device = new Device { DeviceCode = code, Barcode = barcode, Name = code, Quantity = 1,
                    CategoryId = category.Id, DepartmentId = department ?? seed.Department, ParentDeviceId = parent?.Id };
                db.Add(device);
                return device;
            }
            Add("READY");
            Add("NOT-RECEIVED").Quantity = 0;
            Add("LOCKED").Status = EntityStatus.Locked;
            Add("DAMAGED").Condition = DeviceCondition.Damaged;
            Add("MISSING").Condition = DeviceCondition.Missing;
            var blockedRoot = Add("BLOCKED-ROOT");
            var blockedMiddle = Add("BLOCKED-MIDDLE", blockedRoot);
            var busy = Add("BUSY", blockedMiddle);
            db.Add(new DeviceOperationHold { DeviceCode = busy.DeviceCode, DeviceId = busy.Id,
                OperationKind = DeviceOperationKind.Loan, OperationId = Guid.NewGuid(), HeldAt = host.Clock.GetUtcNow() });
            Add("GOOD-CHILD", Add("GOOD-PARENT"));
            Add("DAMAGED-CHILD", Add("RECOVERY-PARENT")).Condition = DeviceCondition.Damaged;
            Add("FOREIGN-CHILD", Add("MIXED-PARENT"), other.Id);
            await db.SaveChangesAsync();
        }
        using var staff = host.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.StaffToken);
        using var departmentStaff = host.CreateClient();
        departmentStaff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('A', 64));
        async Task<JsonElement> List(HttpClient client, string query)
        {
            using var response = await client.GetAsync("/api/v1/devices?" + query);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        static string[] Codes(JsonElement page) => page.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("deviceCode").GetString()!).Order().ToArray();
        Assert.Equal(new[] { "FOREIGN-CHILD", "GOOD-CHILD", "GOOD-PARENT", "MIXED-PARENT", "READY" }, Codes(await List(staff, "eligibleFor=Loan")));
        Assert.Equal(new[] { "FOREIGN-CHILD", "GOOD-CHILD", "GOOD-PARENT", "READY" }, Codes(await List(staff, "eligibleFor=Transfer")));
        Assert.Equal(new[] { "GOOD-CHILD", "GOOD-PARENT", "READY" }, Codes(await List(departmentStaff, "eligibleFor=Transfer")));
        Assert.Empty(Codes(await List(departmentStaff, "eligibleFor=Transfer&departmentCode=OTHER")));
        Assert.Equal(new[] { "DAMAGED", "DAMAGED-CHILD", "FOREIGN-CHILD", "GOOD-CHILD", "GOOD-PARENT", "READY", "RECOVERY-PARENT" }, Codes(await List(staff, "eligibleFor=Recovery")));
        Assert.Equal(new[] { "DAMAGED", "DAMAGED-CHILD", "GOOD-CHILD", "GOOD-PARENT", "READY", "RECOVERY-PARENT" }, Codes(await List(departmentStaff, "eligibleFor=Recovery")));
        Assert.Empty(Codes(await List(departmentStaff, "eligibleFor=Recovery&departmentCode=OTHER")));
        var paged = await List(staff, "eligibleFor=Transfer&departmentCode=PB_TEST&search=GOOD&pageSize=1&page=2&sortBy=Name");
        Assert.Equal(2, paged.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "GOOD-PARENT" }, Codes(paged));
        Assert.Equal(14, (await List(staff, "")).GetProperty("totalCount").GetInt32()); // Existing unfiltered list stays intact.
        using var invalid = await staff.GetAsync("/api/v1/devices?eligibleFor=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
