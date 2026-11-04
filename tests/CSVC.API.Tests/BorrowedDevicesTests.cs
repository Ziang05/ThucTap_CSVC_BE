using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.DeviceLoans;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class BorrowedDevicesTests
{
    private const string List = "/api/v1/devices/borrowed";

    [PostgreSqlFact]
    public async Task Devices_appear_after_actual_receipt_and_stay_until_lender_receives_return_without_expanding_asset_permissions()
    {
        await using var f = await TwoDepartmentLoanTests.Fixture.Create();
        Assert.Equal(0, Count(await Get(f.A, List)));
        var loan = await f.Loan();
        Assert.Equal(0, Count(await Get(f.A, List)));
        await f.Post(f.B, loan, "approve", new { version = 1 });
        Assert.Equal(0, Count(await Get(f.A, List))); // Approved is still awaiting physical receipt.
        await f.Post(f.A, loan, "handover-complete", f.Items(2));

        var borrowed = await Get(f.A, List);
        Assert.Equal(1, Count(borrowed));
        var item = borrowed.GetProperty("items")[0];
        Assert.Equal("READY", item.GetProperty("deviceCode").GetString());
        Assert.Equal("PB_TEST", item.GetProperty("lenderDepartmentCode").GetString());
        Assert.Equal("PB_TEST", item.GetProperty("ownerDepartmentCode").GetString());
        Assert.Equal(loan, item.GetProperty("loanCode").GetString());
        foreach (var field in new[] { "purchasePrice", "paidInvoiceCode", "notes", "responsibleFullName", "barcode" })
            Assert.False(item.TryGetProperty(field, out _));
        Assert.Equal(1, Count(await Get(f.A, "/api/v1/devices"))); // A still owns only OWN.
        using (var detail = await f.A.GetAsync("/api/v1/devices/READY")) Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using (var edit = await f.A.PutAsJsonAsync("/api/v1/devices/READY/lock", new { })) Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(0, Count(await Get(f.C, List + "?departmentCode=A")));
        Assert.Equal(0, Count(await Get(f.Manager, List))); // Global management roles still use their own borrower department here.

        var owned = (await Get(f.B, "/api/v1/devices?search=READY")).GetProperty("items")[0];
        Assert.Equal("A", owned.GetProperty("borrowerDepartmentCode").GetString());
        Assert.Equal(loan, owned.GetProperty("activeLoanCode").GetString());
        Assert.Equal("OnLoan", owned.GetProperty("availability").GetString());

        await f.Post(f.A, loan, "return", f.Items(3));
        var pending = (await Get(f.A, List)).GetProperty("items")[0];
        Assert.Equal("PendingReturnConfirmation", pending.GetProperty("status").GetString());
        f.Host.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, Count(await Get(f.A, List + "?overdue=true&status=PendingReturnConfirmation")));
        Assert.Equal(0, Count(await Get(f.A, List + "?overdue=false")));
        await f.Post(f.B, loan, "reject-return", new { version = 4, reason = "Not received" });
        Assert.Equal(1, Count(await Get(f.A, List + "?status=OnLoan")));
        await f.Post(f.A, loan, "return", f.Items(5));
        await f.Post(f.B, loan, "confirm-return", f.Items(6));
        Assert.Equal(0, Count(await Get(f.A, List)));
        owned = (await Get(f.B, "/api/v1/devices?search=READY")).GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, owned.GetProperty("activeLoanCode").ValueKind);
        await f.Db(async db =>
        {
            var device = await db.Devices.SingleAsync(d => d.DeviceCode == "READY");
            Assert.Equal(f.LenderId, device.DepartmentId);
            Assert.Equal(f.StaffId, device.ResponsibleAccountId);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Borrowed_items_are_filtered_before_count_and_paging_and_include_components()
    {
        await using var f = await TwoDepartmentLoanTests.Fixture.Create();
        var code = await f.Loan("PARENT");
        await f.Post(f.B, code, "approve", new { version = 1 });
        await f.Post(f.A, code, "handover-complete", new { version = 2, items = new[] { new { deviceCode = "PARENT", condition = "Good" }, new { deviceCode = "CHILD", condition = "Good" } } });
        var first = await Get(f.A, List + "?pageSize=1&page=1");
        var second = await Get(f.A, List + "?pageSize=1&page=2");
        Assert.Equal(2, Count(first)); Assert.Equal(2, Count(second));
        Assert.Equal("CHILD", first.GetProperty("items")[0].GetProperty("deviceCode").GetString());
        Assert.Equal("PARENT", second.GetProperty("items")[0].GetProperty("deviceCode").GetString());
        Assert.Equal(1, Count(await Get(f.A, List + "?search=parent&pageSize=1")));
        Assert.Equal(2, Count(await Get(f.A, List + "?loanCode=" + code)));
        Assert.Equal(0, Count(await Get(f.A, List + "?status=PendingReturnConfirmation")));
        Assert.Equal(0, Count(await Get(f.C, List + "?loanCode=" + code)));
    }

    [PostgreSqlFact]
    public async Task Legacy_active_loans_remain_visible_without_inventing_the_historical_lender()
    {
        await using var f = await TwoDepartmentLoanTests.Fixture.Create();
        await f.OnLoan();
        await f.Db(async db => { var loan = await db.DeviceLoans.SingleAsync(); loan.WorkflowVersion = 1; loan.LenderDepartmentId = null; await db.SaveChangesAsync(); });
        var row = (await Get(f.A, List)).GetProperty("items")[0];
        Assert.Equal(1, row.GetProperty("workflowVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lenderDepartmentCode").ValueKind);
        Assert.Equal("PB_TEST", row.GetProperty("ownerDepartmentCode").GetString());
    }

    [PostgreSqlTheory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=Approved")]
    public async Task Invalid_borrowed_list_queries_are_rejected(string query)
    {
        await using var f = await TwoDepartmentLoanTests.Fixture.Create();
        using var response = await f.A.GetAsync(List + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static int Count(JsonElement result) => result.GetProperty("totalCount").GetInt32();
    private static async Task<JsonElement> Get(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
