using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class SourceRequestAssignmentTests
{
    private const string Root = "/api/v1/procurement-requests";
    internal static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    { using (response) { Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    internal static async Task<int> Version(EquipmentStaffWorkflowTests.Fixture f, string code)
        => (await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/" + code)).GetProperty("summary").GetProperty("version").GetInt32();
    internal static async Task Assign(EquipmentStaffWorkflowTests.Fixture f, string code, string employee = "TK_STAFF")
        => await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/assignment", new { requestId = Guid.NewGuid(), version = await Version(f, code), assigneeAccountCode = employee, reason = "Responsible employee updated" }));
    internal static async Task<string> Create(EquipmentStaffWorkflowTests.Fixture f, bool repair = false, bool specialized = false)
        => (await Read(await f.Manager.PostAsJsonAsync(Root + "/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "TEST-" + Guid.NewGuid().ToString("N"), sourceVersion = 1, action = "Create", request = new { requestType = repair ? "Repair" : "Purchase", title = "Inherited work", requestingDepartmentCode = "PB_TARGET", sourceStatus = "Submitted", createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:01:00Z", lines = new[] { new { sourceLineCode = "L1", kind = "Equipment", description = "Requested work", quantity = 1, unit = "Cai", categoryCode = "CAT_ROLE", deviceCode = repair ? "TB-ROLE-001" : null, specialization = specialized ? "Specialized" : "Common" } } } }))).GetProperty("displayCode").GetString()!;
    private static async Task MoveSecondEmployee(EquipmentStaffWorkflowTests.Fixture f)
        => await f.InDb(async db => { var a = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_FOREIGN"); (await db.UserProfiles.SingleAsync(p => p.Id == a.UserProfileId)).DepartmentId = (await db.Departments.SingleAsync(d => d.Code == "PB_TEST")).Id; await db.SaveChangesAsync(); });

    [PostgreSqlFact]
    public async Task Inherited_requests_are_unassigned_and_manager_assignment_is_versioned_audited_and_idempotent()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var code = await Create(f); var route = Root + "/" + code;
        var d = await f.Staff.GetFromJsonAsync<JsonElement>(route); Assert.Equal(JsonValueKind.Null, d.GetProperty("assignment").ValueKind); Assert.False(d.GetProperty("canExecute").GetBoolean());
        await Expect(await f.Staff.PostAsJsonAsync(route + "/status", new { version = await Version(f, code), status = "InProgress" }), HttpStatusCode.Conflict);
        var command = new { requestId = Guid.NewGuid(), version = await Version(f, code), assigneeAccountCode = "TK_STAFF" };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/assignment", command), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/assignment", new { requestId = Guid.NewGuid(), version = command.version, assigneeAccountCode = "TK_FOREIGN" }), HttpStatusCode.BadRequest);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/assignment", command)); await Expect(await f.Manager.PostAsJsonAsync(route + "/assignment", command));
        d = await f.Staff.GetFromJsonAsync<JsonElement>(route); Assert.True(d.GetProperty("canExecute").GetBoolean()); Assert.Equal("TK_STAFF", d.GetProperty("assignment").GetProperty("accountCode").GetString());
        await Expect(await f.Manager.PostAsJsonAsync(route + "/assignment", new { requestId = Guid.NewGuid(), version = command.version, assigneeAccountCode = "TK_STAFF" }), HttpStatusCode.Conflict);
        await f.InDb(async db => Assert.Single(await db.Set<ProcurementRequestEvent>().Where(e => e.Action == "ASSIGNMENT_CHANGED").ToListAsync()));
        Assert.Equal(1, (await f.Staff.GetFromJsonAsync<JsonElement>(Root + "?assignedToMe=true")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await f.Staff.GetFromJsonAsync<JsonElement>(Root + "?unassigned=true")).GetProperty("totalCount").GetInt32());
        var report = await f.Manager.GetFromJsonAsync<JsonElement>(Root + "/workload"); Assert.Equal(1, report[0].GetProperty("open").GetInt32());
        await Expect(await f.Staff.GetAsync(Root + "/workload"), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Reassignment_revokes_previous_staff_and_preserves_manager_approval_and_department_handover()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await MoveSecondEmployee(f); var code = await Create(f); await Assign(f, code);
        var transfer = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new { sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Whole allocation", items = new[] { new { sourceLineCode = "L1", deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.Created);
        var route = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Assign(f, code, "TK_FOREIGN");
        await Expect(await f.Staff.PostAsJsonAsync(route + "/cancel", new { }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/status", new { version = await Version(f, code), status = "InProgress" }), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Root + "/" + code + "/status", new { version = await Version(f, code), status = "InProgress" }));
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null));
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }));
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Root + "/" + code + "/complete", new { version = await Version(f, code) }));
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/assignment", new { requestId = Guid.NewGuid(), version = await Version(f, code), assigneeAccountCode = "TK_STAFF", reason = "Too late" }), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Source_repair_start_is_atomic_keeps_real_starter_and_new_owner_can_record_results()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await MoveSecondEmployee(f); var code = await Create(f, repair: true); await Assign(f, code);
        var command = new { requestId = Guid.NewGuid(), version = await Version(f, code), items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite" } } };
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_start_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action='REPAIR_EXECUTION_STARTED' THEN RAISE EXCEPTION 'Test'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_start_audit BEFORE INSERT ON procurement_request_events FOR EACH ROW EXECUTE FUNCTION fail_start_audit();"));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/start-repair", command), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Empty(await db.ServiceOrders.ToListAsync()); Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.Empty(await db.Set<ProcurementDocumentLink>().ToListAsync()); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_start_audit ON procurement_request_events; DROP FUNCTION fail_start_audit();"); });
        var order = await Read(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/start-repair", command)); var route = "/api/v1/service-orders/" + order.GetProperty("displayCode").GetString();
        Assert.Equal("InProgress", order.GetProperty("status").GetString()); Assert.Equal("TK_STAFF", order.GetProperty("executionStartedByAccountCode").GetString()); Assert.Equal(JsonValueKind.Null, order.GetProperty("approvedByAccountCode").ValueKind);
        await f.InDb(async db => { Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); var hold = await db.Set<ServiceOrderDeviceHold>().SingleAsync(); Assert.Equal((await db.Accounts.SingleAsync(a => a.AccountCode == "TK_STAFF")).Id, hold.HeldByAccountId); await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE service_orders SET source_request_id=NULL, source_request_code=NULL, execution_started_at=NULL, execution_started_by_account_id=NULL")); });
        await Assign(f, code, "TK_FOREIGN");
        var result = new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32(), items = new[] { new { itemNo = 1, result = "Completed", description = "Complete result" } } };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/results", result), HttpStatusCode.Forbidden); await Expect(await f.Manager.PostAsJsonAsync(route + "/results", result), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/results", result));
        await f.InDb(async db => Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()));
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/accept-results", new { requestId = Guid.NewGuid(), version = await Version(f, code), accepted = true }));
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Root + "/" + code + "/complete", new { version = await Version(f, code) }));
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/results", result));
    }

    [PostgreSqlFact]
    public async Task Reassigned_receipt_keeps_manager_approval_and_only_new_owner_receives_into_qttb()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await MoveSecondEmployee(f); var code = await Create(f); await Assign(f, code);
        await Read(await f.Manager.PostAsJsonAsync("/api/v1/suppliers", new { displayCode = "NCC_ASSIGN", name = "Supplier" }), HttpStatusCode.Created);
        var receipt = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", new { sourceRequestCode = code, supplierCode = "NCC_ASSIGN", requestingDepartmentCode = "PB_TARGET", deliveryDate = "2026-10-21", deliveryBatch = "1", items = new[] { new { sourceLineCode = "L1", categoryCode = "CAT_ROLE", name = "New equipment" } } }), HttpStatusCode.Created);
        var route = "/api/v1/device-receipts/" + receipt.GetProperty("receiptCode").GetString();
        await Assign(f, code, "TK_FOREIGN");
        await Expect(await f.Manager.PostAsync(route + "/approve", null));
        await Expect(await f.Staff.PostAsync(route + "/complete", null), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsync(route + "/complete", null));
        await f.InDb(async db => { var item = await db.DeviceReceiptItems.SingleAsync(); var device = await db.Devices.SingleAsync(d => d.Id == item.DeviceId); Assert.Equal((await db.Departments.SingleAsync(d => d.Code == "PB_TEST")).Id, device.DepartmentId); });
    }

    [PostgreSqlFact]
    public async Task Repair_material_documents_follow_current_source_assignee_when_posting_and_returning()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await MoveSecondEmployee(f); var code = await Create(f, repair: true); await Assign(f, code);
        var order = await Read(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/start-repair", new { requestId = Guid.NewGuid(), version = await Version(f, code), items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite" } } }));
        const string materials = "/api/v1/materials";
        await Read(await f.Staff.PostAsJsonAsync(materials + "/units", new { code = "CAI", name = "Piece", decimalPlaces = 0 }));
        await Read(await f.Staff.PostAsJsonAsync(materials, new { code = "VT_ASSIGN", name = "Generic part", unitCode = "CAI", kind = "Material" }));
        var stock = await Read(await f.Staff.PostAsJsonAsync(materials + "/documents", new { requestId = Guid.NewGuid(), kind = "Receipt", reason = "Stock", reference = "Purchase document", lines = new[] { new { materialCode = "VT_ASSIGN", quantity = 3, condition = "New" } } }));
        await Expect(await f.Staff.PostAsJsonAsync(materials + "/documents/" + stock.GetProperty("summary").GetProperty("code").GetString() + "/post", new { requestId = Guid.NewGuid(), version = stock.GetProperty("summary").GetProperty("version").GetInt32() }));
        var issue = await Read(await f.Staff.PostAsJsonAsync(materials + "/documents", new { requestId = Guid.NewGuid(), kind = "Issue", reason = "Repair part", serviceOrderCode = order.GetProperty("displayCode").GetString(), lines = new[] { new { materialCode = "VT_ASSIGN", quantity = 1, condition = "New", deviceCode = "TB-ROLE-001" } } }));
        var route = materials + "/documents/" + issue.GetProperty("summary").GetProperty("code").GetString();
        await Assign(f, code, "TK_FOREIGN");
        var post = new { requestId = Guid.NewGuid(), version = issue.GetProperty("summary").GetProperty("version").GetInt32() };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/post", post), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/post", post));
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/receive", new { requestId = Guid.NewGuid(), version = post.version + 1 }));
        var back = new { requestId = Guid.NewGuid(), kind = "ReturnUnused", reason = "Unused part", lines = new[] { new { materialCode = "VT_ASSIGN", quantity = 1, condition = "New", originalIssueLineId = issue.GetProperty("lines")[0].GetProperty("id").GetGuid() } } };
        await Expect(await f.Staff.PostAsJsonAsync(materials + "/documents", back), HttpStatusCode.Forbidden);
        await Read(await f.ForeignStaff.PostAsJsonAsync(materials + "/documents", back));
    }

    [PostgreSqlFact]
    public async Task A_new_repair_requires_the_source_start_endpoint_and_inactive_staff_cannot_be_assigned()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var code = await Create(f, repair: true); var version = await Version(f, code);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new { requestId = Guid.NewGuid(), orderType = "Repair", items = new[] { new { deviceCode = "TB-ROLE-001", executionMethod = "OnSite" } } }), HttpStatusCode.Conflict);
        await f.InDb(async db => { (await db.Accounts.SingleAsync(a => a.AccountCode == "TK_STAFF")).IsLock = true; await db.SaveChangesAsync(); });
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/assignment", new { requestId = Guid.NewGuid(), version, assigneeAccountCode = "TK_STAFF" }), HttpStatusCode.BadRequest);
    }
}
