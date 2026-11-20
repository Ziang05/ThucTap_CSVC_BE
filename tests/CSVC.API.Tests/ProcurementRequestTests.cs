using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.Suppliers;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class ProcurementRequestTests
{
    private const string Root = "/api/v1/procurement-requests";
    private static readonly DateTimeOffset SourceTime = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
    private static object Line(string code = "L1", int quantity = 1, string category = "CAT_ROLE", string? device = null, string kind = "Equipment")
        => new { sourceLineCode = code, kind, description = "Equipment work", quantity, unit = "Cai", categoryCode = category, deviceCode = device, specialization = "Common" };
    private static object Event(string external, int revision = 1, string action = "Create", object[]? lines = null, string department = "PB_TARGET", string title = "Equipment request", Guid? eventId = null, string type = "Purchase")
        => new { eventId = eventId ?? Guid.NewGuid(), externalCode = external, sourceVersion = revision, action, request = new { requestType = type, title, requestingDepartmentCode = department, sourceStatus = "Submitted", createdAt = SourceTime, updatedAt = SourceTime.AddMinutes(revision), lines = lines ?? [Line()] } };
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        using (response) { Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    private static Task<JsonElement> Send(EquipmentStaffWorkflowTests.Fixture f, object body) => SendCore(f.Manager, body);
    private static async Task<JsonElement> SendCore(HttpClient client, object body) => await Json(await client.PostAsJsonAsync(Root + "/simulated-events", body));
    private static Task<JsonElement> Detail(EquipmentStaffWorkflowTests.Fixture f, string code) => f.Manager.GetFromJsonAsync<JsonElement>(Root + "/" + code);
    private static async Task Close(EquipmentStaffWorkflowTests.Fixture f, string code, HttpStatusCode status = HttpStatusCode.NoContent, string? failure = null)
    { var d = await Detail(f, code); await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/complete", new { version = d.GetProperty("summary").GetProperty("version").GetInt32(), failureReason = failure }), status); }
    private static object Cancel(string external, int revision) => new { eventId = Guid.NewGuid(), externalCode = external, sourceVersion = revision, action = "Cancel", reason = "Source cancellation" };

    [PostgreSqlFact]
    public async Task Concurrent_source_delivery_is_idempotent_and_link_audit_failure_rolls_back_document_and_holds()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var body = Event("PMMS-CONCURRENT");
        var concurrent = await Task.WhenAll(Send(f, body), Send(f, body));
        Assert.Equal(concurrent[0].ToString(), concurrent[1].ToString());
        var replayWithDifferentEventId = await Send(f, Event("PMMS-CONCURRENT"));
        Assert.Equal(concurrent[0].ToString(), replayWithDifferentEventId.ToString());
        var code = concurrent[0].GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        var before = await Detail(f, code);
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_source_link_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action='DOCUMENT_LINKED' THEN RAISE EXCEPTION 'Test source audit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_source_link_audit BEFORE INSERT ON procurement_request_events FOR EACH ROW EXECUTE FUNCTION fail_source_link_audit();
            """));
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new
        {
            sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT",
            reason = "Test atomic source link", items = new[] { new { sourceLineCode = "L1", deviceCode = "TB-ROLE-001" } }
        }), HttpStatusCode.ServiceUnavailable);
        var after = await Detail(f, code);
        Assert.Equal(before.GetProperty("summary").GetProperty("version").GetInt32(), after.GetProperty("summary").GetProperty("version").GetInt32());
        await f.InDb(async db =>
        {
            Assert.Single(await db.Set<ProcurementRequest>().ToListAsync());
            Assert.Equal(2, await db.Set<ProcurementRequestEvent>().CountAsync());
            Assert.Empty(await db.Set<ProcurementDocumentLink>().ToListAsync());
            Assert.Empty(await db.DeviceTransfers.ToListAsync());
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Source_replay_versions_mapping_and_scope_history_are_preserved()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var body = Event("PMMS-REPLAY", lines: [Line(category: "UNKNOWN")]);
        var first = await Send(f, body);
        Assert.Equal("NeedsMapping", first.GetProperty("outcome").GetString());
        var code = first.GetProperty("displayCode").GetString()!;
        var replay = await Send(f, body);
        Assert.Equal(first.ToString(), replay.ToString());
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/simulated-events", Event("PMMS-REPLAY", title: "Conflicting revision")), HttpStatusCode.Conflict);
        var fixedSource = await Send(f, Event("PMMS-REPLAY", 3, "Update", title: "Corrected"));
        Assert.Equal("Applied", fixedSource.GetProperty("outcome").GetString());
        Assert.Equal("IgnoredStale", (await Send(f, Event("PMMS-REPLAY", 2, "Update", title: "Obsolete"))).GetProperty("outcome").GetString());
        var d = await Detail(f, code);
        Assert.True(d.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("Corrected", d.GetProperty("summary").GetProperty("title").GetString());
        Assert.Equal(3, d.GetProperty("summary").GetProperty("sourceVersion").GetInt32());
        Assert.False(d.GetProperty("summary").GetProperty("requiresReview").GetBoolean());
        var history = await f.Manager.GetFromJsonAsync<JsonElement>(Root + "/" + code + "/history");
        Assert.Equal(3, history.GetProperty("totalCount").GetInt32());
        Assert.Contains(history.GetProperty("items").EnumerateArray(), e => e.GetProperty("sourcePayload").GetString()!.Contains("UNKNOWN"));
        await Expect(await f.Department.GetAsync(Root), HttpStatusCode.OK);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/simulated-events", Event("FORBIDDEN")), HttpStatusCode.Forbidden);
        await Close(f, code, HttpStatusCode.Conflict);
        Assert.Equal("Cancelled", (await Send(f, Cancel("PMMS-REPLAY", 4))).GetProperty("outcome").GetString());
        Assert.Equal("IgnoredTerminal", (await Send(f, Event("PMMS-REPLAY", 5, "Update"))).GetProperty("outcome").GetString());
        await f.InDb(async db => { Assert.Single(await db.Set<ProcurementRequest>().ToListAsync()); Assert.Single(await db.Set<ProcurementRequestLine>().ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Existing_stock_requires_whole_allocation_and_actual_handover_before_completion()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var first = await Send(f, Event("PMMS-STOCK", lines: [Line(quantity: 2)]));
        var code = first.GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        object Transfer(params string[] codes) => new { sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Source allocation", items = codes.Select(c => new { sourceLineCode = "L1", deviceCode = c }).ToArray() };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Transfer("TB-ROLE-001")), HttpStatusCode.BadRequest);
        await f.InDb(async db => { Assert.Empty(await db.DeviceTransfers.ToListAsync()); Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Empty(await db.Set<ProcurementDocumentLink>().ToListAsync()); });
        var transfer = await Json(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Transfer("TB-ROLE-001", "TB-ROLE-002")), HttpStatusCode.Created);
        var route = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PutAsJsonAsync(route, Transfer("TB-ROLE-001", "TB-ROLE-002")), HttpStatusCode.Conflict);
        await Close(f, code, HttpStatusCode.Conflict);
        Assert.Equal("CancelRejected", (await Send(f, Cancel("PMMS-STOCK", 2))).GetProperty("outcome").GetString());
        var update = await Send(f, Event("PMMS-STOCK", 3, "Update", [Line(quantity: 1)], title: "Changed scope"));
        Assert.Equal("NeedsReview", update.GetProperty("outcome").GetString());
        var d = await Detail(f, code);
        Assert.Equal(2m, d.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        var review = new { version = d.GetProperty("summary").GetProperty("version").GetInt32(), reason = "Keep agreed existing scope" };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/review-source-update", review), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/review-source-update", review));
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null));
        await Close(f, code, HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" }, new { deviceCode = "TB-ROLE-002", condition = "Good" } } }));
        Assert.Equal("CancelRejected", (await Send(f, Cancel("PMMS-STOCK", 4))).GetProperty("outcome").GetString());
        await Close(f, code);
        d = await Detail(f, code);
        Assert.Equal("Completed", d.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Success", d.GetProperty("result").GetString());
        Assert.Single(d.GetProperty("documents").EnumerateArray());
    }

    [PostgreSqlFact]
    public async Task Receipt_must_enter_QTTB_and_allocation_must_include_all_received_devices()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.InDb(async db => { db.Add(new Supplier { DisplayCode = "SUP_PMMS", Name = "PMMS supplier" }); await db.SaveChangesAsync(); });
        var code = (await Send(f, Event("PMMS-RECEIPT", lines: [Line(quantity: 2)]))).GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        var receipt = await Json(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", new { sourceRequestCode = code, supplierCode = "SUP_PMMS", requestingDepartmentCode = "PB_TARGET", deliveryDate = "2026-10-21", deliveryBatch = "1", items = new[] { new { sourceLineCode = "L1", deviceCode = "PMMS-NEW", name = "New stock", categoryCode = "CAT_ROLE" } } }), HttpStatusCode.Created);
        var receiptRoute = "/api/v1/device-receipts/" + receipt.GetProperty("receiptCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(receiptRoute + "/approve", new { }));
        await Expect(await f.Staff.PostAsync(receiptRoute + "/complete", null));
        await Close(f, code, HttpStatusCode.Conflict);
        object Transfer(string second) => new { sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Allocate whole request", items = new[] { new { sourceLineCode = "L1", deviceCode = "TB-ROLE-001" }, new { sourceLineCode = "L1", deviceCode = second } } };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Transfer("TB-ROLE-002")), HttpStatusCode.BadRequest);
        var transfer = await Json(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Transfer("PMMS-NEW")), HttpStatusCode.Created);
        var route = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null));
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" }, new { deviceCode = "PMMS-NEW", condition = "Good" } } }));
        await Close(f, code);
        var device = await f.Department.GetFromJsonAsync<JsonElement>("/api/v1/devices/PMMS-NEW");
        Assert.Equal("InUse", device.GetProperty("useState").GetString());
    }

    [PostgreSqlFact]
    public async Task Internal_use_is_atomic_and_materials_cannot_be_falsely_completed_as_equipment()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.InDb(async db => { foreach (var device in await db.Devices.Where(d => d.DeviceCode != "TB-ROLE-003").ToListAsync()) device.UseState = DeviceUseState.InStorage; await db.SaveChangesAsync(); });
        var code = (await Send(f, Event("PMMS-INTERNAL", lines: [Line(quantity: 2)], department: "PB_TEST"))).GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        object Use(params string[] codes) => new { sourceRequestCode = code, items = codes.Select(c => new { sourceLineCode = "L1", deviceCode = c, responsibleAccountCode = "TK_STAFF" }) };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/devices/put-into-use", Use("TB-ROLE-001")), HttpStatusCode.BadRequest);
        await f.InDb(async db => Assert.Equal(DeviceUseState.InStorage, (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001")).UseState));
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/devices/put-into-use", Use("TB-ROLE-001", "TB-ROLE-002")));
        await Close(f, code);
        var material = (await Send(f, Event("PMMS-MATERIAL", lines: [Line(kind: "Material")]))).GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, material);
        await Close(f, material, HttpStatusCode.Conflict);
        await Close(f, material, failure: "Cannot supply requested materials");
        Assert.Equal("Failed", (await Detail(f, material)).GetProperty("result").GetString());
    }

    [PostgreSqlTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Repair_scope_is_inherited_and_cannot_cancel_after_execution_starts(int workflowVersion)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = (await Send(f, Event("PMMS-REPAIR", type: "Repair", lines: [Line(device: "TB-ROLE-001")]))).GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        if (workflowVersion == 1) await f.InDb(async db => { (await db.Set<ProcurementRequest>().SingleAsync()).WorkflowVersion = 1; await db.SaveChangesAsync(); });
        var order = await Json(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/start-repair", new { requestId = Guid.NewGuid(), version = (await Detail(f, code)).GetProperty("summary").GetProperty("version").GetInt32(), items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite" } } }));
        var route = "/api/v1/service-orders/" + order.GetProperty("displayCode").GetString();
        Assert.Equal("CancelRejected", (await Send(f, Cancel("PMMS-REPAIR", 2))).GetProperty("outcome").GetString());
        await Close(f, code, HttpStatusCode.Conflict);
        order = await f.Staff.GetFromJsonAsync<JsonElement>(route);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/results", new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32(), items = new[] { new { itemNo = 1, result = "Completed", description = "Upgrade completed" } } }));
        if (workflowVersion >= 2)
        {
            await Close(f, code, HttpStatusCode.Conflict);
            var ready = await Detail(f, code);
            await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/accept-results", new { requestId = Guid.NewGuid(), version = ready.GetProperty("summary").GetProperty("version").GetInt32(), accepted = true }), HttpStatusCode.Forbidden);
            await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/accept-results", new { requestId = Guid.NewGuid(), version = ready.GetProperty("summary").GetProperty("version").GetInt32(), accepted = true }));
        }
        await Close(f, code);
        Assert.Equal("Success", (await Detail(f, code)).GetProperty("result").GetString());
    }

    [PostgreSqlFact]
    public async Task Source_request_round_trips_catalog_codes_supported_by_existing_catalog_api()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var category = "DM" + new string('A', 62);
        await Json(await f.Manager.PostAsJsonAsync("/api/v1/device-categories", new { displayCode = category, name = "Long category" }), HttpStatusCode.Created);
        var result = await Send(f, Event("PMMS-LONG-CATEGORY", lines: [Line(category: category.ToLowerInvariant())]));
        Assert.Equal("Applied", result.GetProperty("outcome").GetString());
        var detail = await Detail(f, result.GetProperty("displayCode").GetString()!);
        Assert.Equal(category, detail.GetProperty("lines")[0].GetProperty("categoryCode").GetString());
        await f.InDb(async db => Assert.Equal(category, (await db.Set<ProcurementRequestLine>().SingleAsync()).CategoryCode));
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/simulated-events", Event("PMMS-TOO-LONG", lines: [Line(category: category + "A")])), HttpStatusCode.BadRequest);
    }
}
