using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Materials;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class MaterialDepartmentStockTests
{
    private const string Root = "/api/v1/materials";
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    { using (response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync()); }
    private static async Task Catalog(EquipmentStaffWorkflowTests.Fixture f)
    {
        await Json(await f.Manager.PostAsJsonAsync(Root + "/units", new { code = "CAI", name = "Piece", decimalPlaces = 0, status = "Active" }));
        await Json(await f.Manager.PostAsJsonAsync(Root, new { code = "VT-QTTB", name = "Service spare", unitCode = "CAI", kind = "Material", status = "Active" }));
    }
    private static string Code(JsonElement d) => d.GetProperty("summary").GetProperty("code").GetString()!;
    private static async Task Act(EquipmentStaffWorkflowTests.Fixture f, string code, string action, HttpClient? client = null)
    {
        var d = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/documents/" + code);
        await Expect(await (client ?? f.Staff).PostAsJsonAsync(Root + "/documents/" + code + "/" + action, new { requestId = Guid.NewGuid(), version = d.GetProperty("summary").GetProperty("version").GetInt32() }));
    }

    [PostgreSqlFact]
    public async Task Staff_can_add_active_units_and_material_types_but_only_manager_can_edit_or_lock_catalogs()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await Expect(await f.Department.PostAsJsonAsync(Root + "/units", new { name = "Unit", decimalPlaces = 0, status = "Active" }), HttpStatusCode.Forbidden);
        var unit = await Json(await f.Staff.PostAsJsonAsync(Root + "/units", new { name = "Piece", decimalPlaces = 0, status = "Active" }));
        var material = await Json(await f.Staff.PostAsJsonAsync(Root, new { name = "New spare type", unitCode = unit.GetProperty("code").GetString(), kind = "Material", status = "Active" }));
        await Expect(await f.Staff.PostAsJsonAsync(Root, new { name = "Locked new type", unitCode = unit.GetProperty("code").GetString(), kind = "Material", status = "Locked" }), HttpStatusCode.Forbidden);
        var edit = new { name = "Revised spare", unitCode = unit.GetProperty("code").GetString(), kind = "Material", status = "Active", version = material.GetProperty("version").GetInt32() };
        await Expect(await f.Staff.PutAsJsonAsync(Root + "/" + material.GetProperty("code").GetString(), edit), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PutAsJsonAsync(Root + "/units/" + unit.GetProperty("code").GetString(), new { name = "Changed unit", decimalPlaces = 1, status = "Active", version = 1 }), HttpStatusCode.Forbidden);
        await Json(await f.Manager.PutAsJsonAsync(Root + "/" + material.GetProperty("code").GetString(), edit));
        var rows = await f.Staff.GetFromJsonAsync<JsonElement>(Root);
        Assert.Equal(1, rows.GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Receipt_issue_return_and_adjustment_work_without_any_warehouse_setup_or_location_input()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await Catalog(f);
        object Body(string kind, decimal quantity, Guid? source = null, Guid? id = null) => new { requestId = id ?? Guid.NewGuid(), kind, reason = "QTTB stock operation", reference = "Actual supplier document", lines = new[] { new { materialCode = "VT-QTTB", quantity, condition = "New", originalIssueLineId = source } } };
        var body = Body("Receipt", 20);
        var receipt = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", body));
        var replay = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", body));
        Assert.Equal(Code(receipt), Code(replay));
        await f.InDb(async db => { Assert.Single(await db.Set<MaterialWarehouse>().ToListAsync()); Assert.Single(await db.Set<MaterialLocation>().ToListAsync()); Assert.Empty(await db.Set<MaterialBalance>().ToListAsync()); });
        await Act(f, Code(receipt), "post");
        var issue = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", Body("Issue", 3)));
        Assert.Equal(receipt.GetProperty("lines")[0].GetProperty("locationCode").GetString(), issue.GetProperty("lines")[0].GetProperty("locationCode").GetString());
        await Act(f, Code(issue), "post");
        var returned = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", Body("ReturnUnused", 1, issue.GetProperty("lines")[0].GetProperty("id").GetGuid())));
        await Act(f, Code(returned), "approve");
        var adjustment = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", Body("Adjustment", -2)));
        Assert.Equal("PendingApproval", adjustment.GetProperty("summary").GetProperty("status").GetString());
        await Act(f, Code(adjustment), "approve", f.Manager); await Act(f, Code(adjustment), "post");
        var balance = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/balances");
        Assert.Equal(16, balance.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        var report = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/report");
        Assert.Equal(16, report.GetProperty("items")[0].GetProperty("closing").GetDecimal());
        await f.InDb(async db => { Assert.Single(await db.Set<MaterialWarehouse>().ToListAsync()); Assert.Single(await db.Set<MaterialLocation>().ToListAsync()); });
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents", Body("Receipt", 1)), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Failure_to_record_receipt_audit_rolls_back_automatic_QTTB_stock_setup_and_retries_safely()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await Catalog(f);
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_material_receipt_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Test audit rollback'; END $$;
            CREATE TRIGGER reject_material_receipt_audit BEFORE INSERT ON material_document_history FOR EACH ROW EXECUTE FUNCTION reject_material_receipt_audit();
            """));
        var body = new { requestId = Guid.NewGuid(), kind = "Receipt", reason = "New stock", reference = "Supplier document", lines = new[] { new { materialCode = "VT-QTTB", quantity = 1, condition = "New" } } };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents", body), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Empty(await db.Set<MaterialWarehouse>().ToListAsync()); Assert.Empty(await db.Set<MaterialLocation>().ToListAsync()); Assert.Empty(await db.Set<MaterialDocument>().ToListAsync()); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_material_receipt_audit ON material_document_history; DROP FUNCTION reject_material_receipt_audit()"); });
        await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", body));
        await f.InDb(async db => { Assert.Single(await db.Set<MaterialWarehouse>().ToListAsync()); Assert.Single(await db.Set<MaterialLocation>().ToListAsync()); });
    }
}
