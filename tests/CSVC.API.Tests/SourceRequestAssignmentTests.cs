using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class SourceRequestAssignmentTests
{
    private const string Root = "/api/v1/procurement-requests";
    internal static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    { using (response) { Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    internal static async Task<int> Version(EquipmentStaffWorkflowTests.Fixture f, string code)
        => (await f.Manager.GetFromJsonAsync<JsonElement>(Root + "/" + code)).GetProperty("summary").GetProperty("version").GetInt32();
    internal static async Task Assign(EquipmentStaffWorkflowTests.Fixture f, string code, string employee = "TK_STAFF")
        => await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/assignment", new { requestId = Guid.NewGuid(), version = await Version(f, code), assigneeAccountCode = employee, reason = "Responsible employee updated" }));
    internal static async Task<string> Create(EquipmentStaffWorkflowTests.Fixture f, bool repair = false, bool specialized = false, string department = "PB_TARGET")
        => (await Read(await f.Manager.PostAsJsonAsync(Root + "/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "TEST-" + Guid.NewGuid().ToString("N"), sourceVersion = 1, action = "Create", request = new { requestType = repair ? "Repair" : "Purchase", title = "Inherited work", requestingDepartmentCode = department, sourceStatus = "Submitted", createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:01:00Z", lines = new[] { new { sourceLineCode = "L1", kind = "Equipment", description = "Requested work", quantity = 1, unit = "Cai", categoryCode = "CAT_ROLE", deviceCode = repair ? "TB-ROLE-001" : null, specialization = specialized ? "Specialized" : "Common" } } } }))).GetProperty("displayCode").GetString()!;
    private static async Task MoveSecondEmployee(EquipmentStaffWorkflowTests.Fixture f)
        => await f.InDb(async db => { var a = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_FOREIGN"); (await db.UserProfiles.SingleAsync(p => p.Id == a.UserProfileId)).DepartmentId = (await db.Departments.SingleAsync(d => d.Code == "PB_TEST")).Id; await db.SaveChangesAsync(); });

    [PostgreSqlFact]
    public async Task Damage_session_keeps_failed_repairs_and_closes_on_a_successful_order_result()
    {
        const string deviceCode = "TB-ROLE-001";
        const string reports = "/api/v1/damage-reports";
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var report = await Read(await f.Manager.PostAsJsonAsync(reports,
            new { requestId = Guid.NewGuid(), deviceCode, incidentType = "DAMAGE", incidentDescription = "Power failure" }), HttpStatusCode.Created);
        var checkedReport = await Read(await f.Manager.PostAsJsonAsync(reports + "/" + report.GetProperty("reportCode").GetString() + "/verify",
            new { requestId = Guid.NewGuid(), expectedVersion = 1, outcome = "ConfirmedDamaged", reason = "Confirmed during inspection" }));
        var sessionId = checkedReport.GetProperty("damageSessionId").GetGuid();
        var sessionRoute = $"/api/v1/damage-sessions/{sessionId}";

        async Task<string> StartAndRecord(string result)
        {
            var code = await Create(f, repair: true);
            await Assign(f, code);
            var version = await Version(f, code);
            string route = Root + "/" + code + "/start-repair";
            await Expect(await f.Staff.PostAsJsonAsync(route, new { requestId = Guid.NewGuid(), version,
                items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite", damageSessionId = Guid.NewGuid() } } }), HttpStatusCode.Conflict);
            var order = await Read(await f.Staff.PostAsJsonAsync(route, new { requestId = Guid.NewGuid(), version,
                items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite", damageSessionId = sessionId } } }));
            var item = Assert.Single(order.GetProperty("items").EnumerateArray());
            Assert.Equal(sessionId, item.GetProperty("damageSessionId").GetGuid());
            var orderCode = order.GetProperty("displayCode").GetString()!;
            var command = new { requestId = Guid.NewGuid(), items = new[] { new { itemNo = 1, version = item.GetProperty("version").GetInt32(),
                result, description = "Professional result" } } };
            await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders/" + orderCode + "/results", command));
            await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders/" + orderCode + "/results", command));
            return orderCode;
        }

        var failedOrder = await StartAndRecord("Failed");
        var afterFailure = await Read(await f.Manager.GetAsync(sessionRoute));
        Assert.Equal(JsonValueKind.Null, afterFailure.GetProperty("closedAt").ValueKind);
        Assert.True(afterFailure.GetProperty("canCreateRepair").GetBoolean());
        Assert.Equal("Failed", Assert.Single(afterFailure.GetProperty("attempts").EnumerateArray()).GetProperty("itemStatus").GetString());

        var completedOrder = await StartAndRecord("Completed");
        var closed = await Read(await f.Manager.GetAsync(sessionRoute));
        var attempts = closed.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal(completedOrder, attempts[0].GetProperty("orderCode").GetString());
        Assert.Equal(failedOrder, attempts[1].GetProperty("orderCode").GetString());
        Assert.Equal("Completed", attempts[0].GetProperty("itemStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);
        Assert.False(closed.GetProperty("canCreateRepair").GetBoolean());
        Assert.Empty(closed.GetProperty("inspections").EnumerateArray());
        await f.InDb(async db =>
        {
            Assert.Equal(CSVC.Core.Devices.DeviceCondition.Good,
                (await db.Devices.SingleAsync(d => d.DeviceCode == deviceCode)).Condition);
            var audit = await db.DeviceHistories.SingleAsync(h => h.ActionCode == "DEVICE_DAMAGE_RESOLVED");
            Assert.Equal(attempts[0].GetProperty("itemId").GetGuid(), audit.RequestId);
        });
    }

    [PostgreSqlFact]
    public async Task External_damage_session_closes_only_after_return_and_a_successful_result()
    {
        const string deviceCode = "TB-ROLE-001";
        const string reports = "/api/v1/damage-reports";
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var report = await Read(await f.Manager.PostAsJsonAsync(reports,
            new { requestId = Guid.NewGuid(), deviceCode, incidentType = "DAMAGE", incidentDescription = "Broken bearing" }), HttpStatusCode.Created);
        var confirmed = await Read(await f.Manager.PostAsJsonAsync(reports + "/" + report.GetProperty("reportCode").GetString() + "/verify",
            new { requestId = Guid.NewGuid(), expectedVersion = 1, outcome = "ConfirmedDamaged", reason = "Mechanical failure" }));
        var sessionId = confirmed.GetProperty("damageSessionId").GetGuid();
        var sessionRoute = $"/api/v1/damage-sessions/{sessionId}";
        var source = await Create(f, repair: true);
        await Assign(f, source);
        var order = await Read(await f.Staff.PostAsJsonAsync(Root + "/" + source + "/start-repair",
            new { requestId = Guid.NewGuid(), version = await Version(f, source), items = new[] { new { sourceLineCode = "L1", executionMethod = "External", damageSessionId = sessionId } } }));
        var orderCode = order.GetProperty("displayCode").GetString();
        await Read(await f.Manager.PostAsJsonAsync("/api/v1/suppliers", new { displayCode = "NCC_DAMAGE", name = "Repair workshop" }), HttpStatusCode.Created);
        var exportRequest = new { requestId = Guid.NewGuid(), serviceOrderCode = orderCode, facilityCode = "NCC_DAMAGE", itemNos = new[] { 1 } };
        await Expect(await f.Department.PostAsJsonAsync("/api/v1/export-slips", exportRequest), HttpStatusCode.Forbidden);
        var export = await Read(await f.Manager.PostAsJsonAsync("/api/v1/export-slips", exportRequest), HttpStatusCode.Created);
        Assert.Equal("Completed", export.GetProperty("status").GetString());
        string exportCode = export.GetProperty("displayCode").GetString()!;

        var resultPath = "/api/v1/service-orders/" + orderCode + "/results";
        var result = new { requestId = Guid.NewGuid(), items = new[] { new { itemNo = 1, result = "Completed", description = "Bearing replaced" } } };
        await Expect(await f.Staff.PostAsJsonAsync(resultPath, result), HttpStatusCode.Conflict);

        var awaiting = await Read(await f.Manager.GetAsync(sessionRoute));
        Assert.True(awaiting.GetProperty("awaitingReturn").GetBoolean());
        Assert.Equal(JsonValueKind.Null, awaiting.GetProperty("closedAt").ValueKind);
        Assert.False(awaiting.GetProperty("canCreateRepair").GetBoolean());
        await Read(await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips",
            new { requestId = Guid.NewGuid(), exportSlipCode = exportCode, handoverPersonName = "Courier", receivedByAccountCode = "TK_MANAGER",
                receivedAt = "2026-10-21T11:00:00Z", items = new[] { new { itemNo = 1, conditionAtReturn = "Good" } } }), HttpStatusCode.Created);
        var open = await Read(await f.Manager.GetAsync(sessionRoute));
        Assert.Equal(JsonValueKind.Null, open.GetProperty("closedAt").ValueKind);
        Assert.False(open.GetProperty("awaitingReturn").GetBoolean());
        await f.InDb(async db => Assert.Equal(CSVC.Core.Devices.DeviceCondition.Damaged,
            (await db.Devices.SingleAsync(d => d.DeviceCode == deviceCode)).Condition));

        await Expect(await f.Staff.PostAsJsonAsync(resultPath, result));
        var closed = await Read(await f.Manager.GetAsync(sessionRoute));
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);
        Assert.False(closed.GetProperty("awaitingReturn").GetBoolean());
        Assert.Empty(closed.GetProperty("inspections").EnumerateArray());
        await f.InDb(async db => Assert.Equal(CSVC.Core.Devices.DeviceCondition.Good,
            (await db.Devices.SingleAsync(d => d.DeviceCode == deviceCode)).Condition));
    }

    [PostgreSqlFact]
    public async Task Staff_see_only_current_assignments_in_lists_search_paging_details_and_history()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await MoveSecondEmployee(f);
        var unassigned = await Create(f); var mine = await Create(f); var other = await Create(f, department: "PB_TEST");
        await Assign(f, mine); await Assign(f, other, "TK_FOREIGN");
        foreach (var suffix in new[] { "", "?assignedToMe=false", "?search=Inherited", "?pageSize=1" })
        {
            var page = await f.Staff.GetFromJsonAsync<JsonElement>(Root + suffix);
            Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(mine, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("displayCode").GetString());
        }
        var next = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "?page=2&pageSize=1"); Assert.Equal(1, next.GetProperty("totalCount").GetInt32()); Assert.Empty(next.GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await f.Staff.GetFromJsonAsync<JsonElement>(Root + "?unassigned=true")).GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await f.Manager.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await f.Department.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        foreach (var hidden in new[] { unassigned, other })
            foreach (var suffix in new[] { "", "/history" })
            {
                await Expect(await f.Staff.GetAsync(Root + "/" + hidden + suffix), HttpStatusCode.NotFound);
                await Expect(await f.Manager.GetAsync(Root + "/" + hidden + suffix), HttpStatusCode.OK);
            }
        await Expect(await f.Staff.GetAsync(Root + "/" + mine), HttpStatusCode.OK);
        await Expect(await f.Staff.GetAsync(Root + "/" + mine + "/history"), HttpStatusCode.OK);
        await Expect(await f.ForeignStaff.GetAsync(Root + "/" + mine), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "DENIED", sourceVersion = 1, action = "Cancel", reason = "Staff cannot access source simulator" }), HttpStatusCode.Forbidden);
        await Assign(f, mine, "TK_FOREIGN");
        Assert.Equal(0, (await f.Staff.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await f.ForeignStaff.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        await Expect(await f.Staff.GetAsync(Root + "/" + mine), HttpStatusCode.NotFound);
        await Expect(await f.Staff.GetAsync(Root + "/" + mine + "/history"), HttpStatusCode.NotFound);
        await Expect(await f.ForeignStaff.GetAsync(Root + "/" + mine), HttpStatusCode.OK);
    }

    [PostgreSqlFact]
    public async Task Inherited_requests_are_unassigned_and_manager_assignment_is_versioned_audited_and_idempotent()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var code = await Create(f); var route = Root + "/" + code;
        var d = await f.Manager.GetFromJsonAsync<JsonElement>(route); Assert.Equal(JsonValueKind.Null, d.GetProperty("assignment").ValueKind); Assert.False(d.GetProperty("canExecute").GetBoolean());
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
    public async Task Quick_repair_creates_one_pending_order_with_the_selected_session_for_each_device()
    {
        const string reports = "/api/v1/damage-reports";
        const string sessions = "/api/v1/damage-sessions";
        const string orders = "/api/v1/service-orders";
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var expected = new Dictionary<string, Guid>();
        foreach (var deviceCode in new[] { "TB-ROLE-001", "TB-ROLE-002" })
        {
            var report = await Read(await f.Manager.PostAsJsonAsync(reports,
                new { requestId = Guid.NewGuid(), deviceCode, incidentType = "DAMAGE", incidentDescription = "Device failed" }), HttpStatusCode.Created);
            var verified = await Read(await f.Manager.PostAsJsonAsync(reports + "/" + report.GetProperty("reportCode").GetString() + "/verify",
                new { requestId = Guid.NewGuid(), expectedVersion = 1, outcome = "ConfirmedDamaged", reason = "Verified failure" }));
            expected.Add(deviceCode, verified.GetProperty("damageSessionId").GetGuid());
        }

        var available = (await Read(await f.Staff.GetAsync(sessions + "?status=Open"))).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, available.Length);
        Assert.All(available, session => Assert.True(session.GetProperty("canCreateRepair").GetBoolean()));

        var invalid = new
        {
            requestId = Guid.NewGuid(), orderType = "Repair",
            items = expected.Select(item => new { deviceCode = item.Key, executionMethod = "OnSite", damageSessionId = expected.Values.First() })
        };
        await Expect(await f.Staff.PostAsJsonAsync(orders, invalid), HttpStatusCode.Conflict);
        await f.InDb(async db => Assert.Empty(await db.ServiceOrders.ToListAsync()));

        var created = await Read(await f.Staff.PostAsJsonAsync(orders, new
        {
            requestId = Guid.NewGuid(), orderType = "Repair",
            items = expected.Select(item => new { deviceCode = item.Key, executionMethod = "OnSite", damageSessionId = item.Value })
        }), HttpStatusCode.Created);
        Assert.Equal("PendingApproval", created.GetProperty("status").GetString());
        Assert.Equal(2, created.GetProperty("items").GetArrayLength());
        foreach (var item in created.GetProperty("items").EnumerateArray())
            Assert.Equal(expected[item.GetProperty("deviceCode").GetString()!], item.GetProperty("damageSessionId").GetGuid());
        var blocked = (await Read(await f.Staff.GetAsync(sessions + "?status=Open"))).GetProperty("items").EnumerateArray().ToArray();
        Assert.All(blocked, session =>
        {
            Assert.False(session.GetProperty("canCreateRepair").GetBoolean());
            Assert.Equal("ActiveRepair", session.GetProperty("repairBlock").GetString());
        });

        var orderRoute = orders + "/" + created.GetProperty("displayCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(orderRoute + "/approve", new { requestId = Guid.NewGuid() }));
        var approved = await Read(await f.Staff.GetAsync(orderRoute));
        var results = approved.GetProperty("items").EnumerateArray().Select(item => new
        {
            itemNo = item.GetProperty("itemNo").GetInt32(), version = item.GetProperty("version").GetInt32(),
            result = "Completed", description = "Repair completed"
        }).ToArray();
        await Expect(await f.Staff.PostAsJsonAsync(orderRoute + "/results", new { requestId = Guid.NewGuid(), items = results }));
        var closed = (await Read(await f.Staff.GetAsync(sessions + "?status=Closed"))).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, closed.Length);
        await f.InDb(async db =>
        {
            var audits = db.DeviceHistories.Where(h => h.ActionCode == "DEVICE_DAMAGE_RESOLVED");
            Assert.Equal(2, await audits.CountAsync());

            // Reproduce records written by the old workflow before applying the reconciliation migration.
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261127034014_DeviceDamageSessions");
            await audits.ExecuteDeleteAsync();
            var sessionRows = db.Set<CSVC.Core.DamageReports.DeviceDamageSession>();
            await sessionRows.ExecuteUpdateAsync(update => update
                .SetProperty(s => s.ClosedAt, (DateTimeOffset?)null)
                .SetProperty(s => s.ClosedByAccountId, (Guid?)null));
            await db.Devices.Where(d => d.DeviceCode == "TB-ROLE-001" || d.DeviceCode == "TB-ROLE-002")
                .ExecuteUpdateAsync(update => update.SetProperty(d => d.Condition, CSVC.Core.Devices.DeviceCondition.Damaged));

            var rejected = await db.ServiceOrderItems.SingleAsync(i => i.DeviceCodeSnapshot == "TB-ROLE-002");
            db.Set<CSVC.Core.DamageReports.DamageSessionInspection>().Add(new()
            {
                RequestId = Guid.NewGuid(), DamageSessionId = rejected.DamageSessionId!.Value, ServiceOrderItemId = rejected.Id,
                ExpectedVersion = 1, Passed = false, Notes = "Legacy validation failed",
                InspectedAt = rejected.ResultRecordedAt!.Value, InspectedByAccountId = rejected.ResultRecordedByAccountId!.Value
            });
            await db.SaveChangesAsync();
            await migrator.MigrateAsync();

            var remaining = await sessionRows.SingleAsync(s => s.ClosedAt == null);
            Assert.Equal(rejected.DamageSessionId, remaining.Id);
            var audit = await audits.SingleAsync();
            Assert.Equal(expected["TB-ROLE-001"], (await sessionRows.SingleAsync(s => s.DeviceId == audit.DeviceId)).Id);
            Assert.Equal(CSVC.Core.Devices.DeviceCondition.Good, (await db.Devices.SingleAsync(d => d.Id == audit.DeviceId)).Condition);
        });
    }

    [PostgreSqlFact]
    public async Task Standalone_repair_waits_for_approval_and_links_to_the_open_damage_session()
    {
        const string deviceCode = "TB-ROLE-001";
        const string reports = "/api/v1/damage-reports";
        const string orders = "/api/v1/service-orders";
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var report = await Read(await f.Manager.PostAsJsonAsync(reports,
            new { requestId = Guid.NewGuid(), deviceCode, incidentType = "DAMAGE", incidentDescription = "Damaged board" }), HttpStatusCode.Created);
        var verification = await Read(await f.Manager.PostAsJsonAsync(reports + "/" + report.GetProperty("reportCode").GetString() + "/verify",
            new { requestId = Guid.NewGuid(), expectedVersion = 1, outcome = "ConfirmedDamaged", reason = "Fault verified" }));
        var sessionId = verification.GetProperty("damageSessionId").GetGuid();
        var command = new { requestId = Guid.NewGuid(), orderType = "Repair",
            items = new[] { new { deviceCode, executionMethod = "OnSite", workDescription = "Repair the board" } } };

        await Expect(await f.Department.PostAsJsonAsync(orders, command), HttpStatusCode.Forbidden);
        var order = await Read(await f.Staff.PostAsJsonAsync(orders, command), HttpStatusCode.Created);
        var code = order.GetProperty("displayCode").GetString();
        Assert.Equal("PendingApproval", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("sourceRequestCode").ValueKind);
        Assert.Equal(sessionId, order.GetProperty("items")[0].GetProperty("damageSessionId").GetGuid());
        Assert.Equal(code, (await Read(await f.Staff.PostAsJsonAsync(orders, command), HttpStatusCode.Created)).GetProperty("displayCode").GetString());
        await Expect(await f.Staff.PostAsJsonAsync(orders,
            new { requestId = Guid.NewGuid(), orderType = "Repair", items = command.items }), HttpStatusCode.Conflict);

        var route = orders + "/" + code;
        var approval = new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32() };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", approval), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", approval));
        var approved = await Read(await f.Staff.GetAsync(route));
        Assert.Equal("InProgress", approved.GetProperty("status").GetString());

        var itemVersion = approved.GetProperty("items")[0].GetProperty("version").GetInt32();
        await Expect(await f.Staff.PostAsJsonAsync(route + "/results", new
        {
            requestId = Guid.NewGuid(), items = new[] { new { itemNo = 1, version = itemVersion, result = "Failed", description = "Replacement needed" } }
        }));
        var episode = await Read(await f.Manager.GetAsync($"/api/v1/damage-sessions/{sessionId}"));
        var effort = Assert.Single(episode.GetProperty("attempts").EnumerateArray());
        Assert.Equal(code, effort.GetProperty("orderCode").GetString());
        Assert.Equal("Failed", effort.GetProperty("itemStatus").GetString());
        var replacement = await Read(await f.Staff.PostAsJsonAsync(orders,
            new { requestId = Guid.NewGuid(), orderType = "Repair", items = command.items }), HttpStatusCode.Created);
        Assert.Equal(sessionId, replacement.GetProperty("items")[0].GetProperty("damageSessionId").GetGuid());
    }

    [PostgreSqlFact]
    public async Task A_PMMS_linked_repair_requires_the_source_start_endpoint_and_inactive_staff_cannot_be_assigned()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var code = await Create(f, repair: true); var version = await Version(f, code);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new { requestId = Guid.NewGuid(), orderType = "Repair", sourceRequestCode = code,
            items = new[] { new { sourceLineCode = "L1", deviceCode = "TB-ROLE-001", executionMethod = "OnSite" } } }), HttpStatusCode.Conflict);
        await f.InDb(async db => { (await db.Accounts.SingleAsync(a => a.AccountCode == "TK_STAFF")).IsLock = true; await db.SaveChangesAsync(); });
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/assignment", new { requestId = Guid.NewGuid(), version, assigneeAccountCode = "TK_STAFF" }), HttpStatusCode.BadRequest);
    }
}
