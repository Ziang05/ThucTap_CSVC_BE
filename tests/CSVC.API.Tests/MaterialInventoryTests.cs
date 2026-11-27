using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Materials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class MaterialInventoryTests
{
    private const string Root = "/api/v1/materials";
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    { using (response) { Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync()); }
    private static async Task<string> Catalog(EquipmentStaffWorkflowTests.Fixture f, string kind = "Material", int decimals = 0)
    {
        await Json(await f.Manager.PostAsJsonAsync(Root + "/units", new { code = "CAI", name = "Piece", decimalPlaces = decimals, status = "Active" }));
        await Json(await f.Manager.PostAsJsonAsync(Root, new { code = "VT1", name = "Replacement part", unitCode = "CAI", kind, status = "Active" }));
        await Json(await f.Manager.PostAsJsonAsync(Root + "/warehouses", new { code = "K1", name = "Equipment warehouse", status = "Active" }));
        var locations = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/locations");
        return locations.GetProperty("items")[0].GetProperty("code").GetString()!;
    }
    private static object Line(string location, decimal qty, Guid? issue = null, Guid? removed = null, string? device = null) => new { materialCode = "VT1", locationCode = location, quantity = qty, originalIssueLineId = issue, removedMaterialId = removed, deviceCode = device, condition = removed is not null ? "Reusable" : "New", conditionNote = removed is not null ? "Tested reusable" : null };
    private static async Task<JsonElement> Doc(EquipmentStaffWorkflowTests.Fixture f, string kind, object[] lines, string? workCode = null, string? target = null)
        => await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind, reason = "Business stock operation", reference = "Actual source document", serviceOrderCode = string.IsNullOrWhiteSpace(workCode) ? null : workCode, targetDepartmentCode = target, lines }));
    private static string Code(JsonElement doc) => doc.GetProperty("summary").GetProperty("code").GetString()!;
    private static Task<JsonElement> Get(EquipmentStaffWorkflowTests.Fixture f, string code) => f.Staff.GetFromJsonAsync<JsonElement>(Root + "/documents/" + code);
    private static async Task Act(EquipmentStaffWorkflowTests.Fixture f, string code, string action, HttpClient? client = null, HttpStatusCode expected = HttpStatusCode.NoContent, Guid? requestId = null)
    {
        var doc = await Get(f, code);
        await Expect(await (client ?? f.Staff).PostAsJsonAsync(Root + "/documents/" + code + "/" + action, new { requestId = requestId ?? Guid.NewGuid(), version = doc.GetProperty("summary").GetProperty("version").GetInt32(), reason = "Document action" }), expected);
    }
    private static async Task<string> Stock(EquipmentStaffWorkflowTests.Fixture f, string location, decimal qty = 20)
    { var doc = await Doc(f, "Receipt", [Line(location, qty)]); var code = Code(doc); await Act(f, code, "post"); return code; }
    private static Task<string> Manual(EquipmentStaffWorkflowTests.Fixture f) => Task.FromResult("");

    [PostgreSqlFact]
    public async Task Material_receipt_accepts_the_existing_supplier_catalog_code_limit()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f);
        var supplier = "NCC" + new string('B', 61);
        await Json(await f.Manager.PostAsJsonAsync("/api/v1/suppliers", new { displayCode = supplier, name = "Long supplier" }), HttpStatusCode.Created);
        var doc = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new
        {
            requestId = Guid.NewGuid(), kind = "Receipt", reason = "Supplier delivery",
            supplierCode = supplier.ToLowerInvariant(), lines = new[] { Line(location, 1) }
        }));
        Assert.Equal(supplier, doc.GetProperty("supplierCode").GetString());
        await Act(f, Code(doc), "post");
        Assert.Equal(supplier, (await Get(f, Code(doc))).GetProperty("supplierCode").GetString());
    }

    [PostgreSqlTheory]
    [InlineData("Repair", false)]
    [InlineData("Maintenance", false)]
    [InlineData("Repair", true)]
    public async Task Professional_results_use_existing_service_history_without_duplicate_interventions(string orderType, bool detailed)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        JsonElement order;
        if (orderType == "Repair")
        {
            var source = await SourceRequestAssignmentTests.Create(f, repair: true); await SourceRequestAssignmentTests.Assign(f, source);
            order = await Json(await f.Staff.PostAsJsonAsync("/api/v1/procurement-requests/" + source + "/start-repair", new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, source), items = new[] { new { sourceLineCode = "L1", executionMethod = "OnSite" } } }));
        }
        else
        {
            order = await Json(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new { requestId = Guid.NewGuid(), orderType, items = new[] { new { deviceCode = "TB-ROLE-001", executionMethod = "OnSite", workDescription = "Inspect equipment" } } }), HttpStatusCode.Created);
            await Expect(await f.Manager.PostAsJsonAsync("/api/v1/service-orders/" + order.GetProperty("displayCode").GetString() + "/approve", new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32() }));
        }
        var code = order.GetProperty("displayCode").GetString(); var route = "/api/v1/service-orders/" + code;
        order = await f.Staff.GetFromJsonAsync<JsonElement>(route);
        var version = order.GetProperty("version").GetInt32();
        if (detailed)
        {
            await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", new
            {
                requestId = Guid.NewGuid(), serviceOrderCode = code, deviceCode = "TB-ROLE-001", kind = "Upgrade",
                target = "Replaceable component", beforeValue = "Previous specification", afterValue = "Upgraded specification", reason = "Requested upgrade"
            }));
            await Expect(await f.Staff.PostAsJsonAsync(route + "/results", new
            {
                requestId = Guid.NewGuid(), version,
                items = new[] { new { itemNo = 1, result = "NotExecuted", description = "Cancelled" } }
            }), HttpStatusCode.BadRequest);
            await f.InDb(async db =>
            {
                Assert.Single(await db.Set<CSVC.Core.ServiceOrders.ServiceOrderDeviceHold>().ToListAsync());
                Assert.False(await db.ServiceOrderHistories.AnyAsync(h => h.ActionCode == "ITEM_NOTEXECUTED"));
            });
        }
        var result = new { requestId = Guid.NewGuid(), version, items = new[] { new { itemNo = 1, result = "Completed", description = "Work finished" } } };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/results", result));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/results", result));
        await f.InDb(async db =>
        {
            Assert.Equal("Work finished", (await db.ServiceOrderItems.SingleAsync()).ResultDescription);
            Assert.Equal(1, await db.ServiceOrderHistories.CountAsync(h => h.ActionCode == "ITEM_COMPLETED"));
            Assert.Equal(1, await db.ServiceOrderHistories.CountAsync(h => h.ActionCode == "ORDER_COMPLETED"));
            Assert.Equal(detailed ? 1 : 0, await db.Set<EquipmentChange>().CountAsync());
            Assert.False(await db.Set<EquipmentChange>().AnyAsync(c => c.Kind == EquipmentChangeKind.WorkResult));
        });
    }

    [PostgreSqlFact]
    public async Task Receipt_twenty_issue_three_keeps_devices_individual_and_reports_seventeen()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); await Stock(f, location); var work = await Manual(f);
        var issue = await Doc(f, "Issue", [Line(location, 3)], work); var code = Code(issue);
        await Act(f, code, "post");
        var balance = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/balances"); Assert.Equal(17, balance.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        var report = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/report"); var row = report.GetProperty("items")[0];
        Assert.Equal(20, row.GetProperty("receipts").GetDecimal()); Assert.Equal(3, row.GetProperty("issues").GetDecimal()); Assert.Equal(17, row.GetProperty("closing").GetDecimal());
        var movements = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/movements"); Assert.Equal(2, movements.GetProperty("totalCount").GetInt32());
        await f.InDb(async db => { Assert.False(db.Database.HasPendingModelChanges()); Assert.False(await db.Devices.AnyAsync(d => d.Quantity > 1)); });
    }
    [PostgreSqlFact]
    public async Task Internal_and_department_material_issues_complete_on_post_and_never_require_receipt_confirmation()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); await Stock(f, location);
        foreach (var target in new string?[] { null, "PB_TARGET" })
        {
            var issue = await Doc(f, "Issue", [Line(location, 3)], target: target);
            var code = Code(issue);
            var body = new { requestId = Guid.NewGuid(), version = 1 };
            await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents/" + code + "/post", body));
            await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents/" + code + "/post", body));
            var posted = await Get(f, code);
            Assert.Equal("Completed", posted.GetProperty("summary").GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, posted.GetProperty("receivedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, posted.GetProperty("receivedByAccountCode").ValueKind);
            await Act(f, code, "receive", target is null ? f.Staff : f.Department, HttpStatusCode.BadRequest);
            await Json(await (target is null ? f.Staff : f.Department).PostAsJsonAsync(Root + "/uses", new { requestId = Guid.NewGuid(), issueLineId = issue.GetProperty("lines")[0].GetProperty("id").GetGuid(), quantity = 1, reason = "Use immediately after posting" }));
        }
        await f.InDb(async db =>
        {
            Assert.Equal(14, (await db.Set<MaterialBalance>().SingleAsync()).Quantity);
            Assert.Equal(3, await db.Set<MaterialLedgerEntry>().CountAsync());
            Assert.False(await db.Set<MaterialDocumentHistory>().AnyAsync(h => h.Action == "RECEIVE"));
        });
    }
    [PostgreSqlFact]
    public async Task Employees_record_without_assignment_and_only_adjustment_needs_manager_approval()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); var doc = await Doc(f, "Receipt", [Line(location, 4)]); var code = Code(doc);
        await Act(f, code, "approve", f.Manager, HttpStatusCode.BadRequest);
        await Act(f, code, "post", f.Manager, HttpStatusCode.Forbidden); await Act(f, code, "post");
        var adjustment = Code(await Doc(f, "Adjustment", [Line(location, -1)])); await Act(f, adjustment, "post", expected: HttpStatusCode.BadRequest); await Act(f, adjustment, "approve", expected: HttpStatusCode.Forbidden);
        await Act(f, adjustment, "approve", f.Manager); await Act(f, adjustment, "post");
        await Expect(await f.Department.GetAsync(Root + "/balances"), HttpStatusCode.Forbidden);
    }
    [PostgreSqlFact]
    public async Task Negative_stock_rolls_back_all_lines_and_duplicate_post_is_idempotent()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); await Stock(f, location, 5);
        var code = Code(await Doc(f, "Issue", [Line(location, 3), Line(location, 3)], target: "PB_TARGET")); await Act(f, code, "post", expected: HttpStatusCode.Conflict);
        await f.InDb(async db => { Assert.Equal(5, (await db.Set<MaterialBalance>().SingleAsync()).Quantity); Assert.Single(await db.Set<MaterialLedgerEntry>().ToListAsync()); });
        var receipt = Code(await Doc(f, "Receipt", [Line(location, 2)])); var body = new { requestId = Guid.NewGuid(), version = 1 }; var route = Root + "/documents/" + receipt + "/post";
        await Expect(await f.Staff.PostAsJsonAsync(route, body)); await Expect(await f.Staff.PostAsJsonAsync(route, body));
        await f.InDb(async db => Assert.Equal(7, (await db.Set<MaterialBalance>().SingleAsync()).Quantity));
    }
    [PostgreSqlFact]
    public async Task Unused_returns_cannot_exceed_the_quantity_remaining_after_consumption()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); await Stock(f, location); var work = await Manual(f);
        var issue = await Doc(f, "Issue", [Line(location, 3)], work); var code = Code(issue); var line = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Act(f, code, "post");
        await Json(await f.Staff.PostAsJsonAsync(Root + "/uses", new { requestId = Guid.NewGuid(),  issueLineId = line, quantity = 2, reason = "Used for preparation" }));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "ReturnUnused", reason = "Too many unused parts", lines = new[] { Line(location, 2, line) } }), HttpStatusCode.Conflict);
        var returned = Code(await Doc(f, "ReturnUnused", [Line(location, 1, line)])); await Act(f, returned, "approve");
        await f.InDb(async db => Assert.Equal(18, (await db.Set<MaterialBalance>().SingleAsync()).Quantity));
    }
    [PostgreSqlFact]
    public async Task Tools_are_consumed_without_mandatory_return_and_can_be_returned_later()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f, "Tool"); await Stock(f, location, 2); var work = await Manual(f);
        var issue = await Doc(f, "Issue", [Line(location, 1)], work); var code = Code(issue); var line = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Act(f, code, "post");
        await Json(await f.Staff.PostAsJsonAsync(Root + "/uses", new { requestId = Guid.NewGuid(),  issueLineId = line, quantity = 1, reason = "Tool issued for work" })); var back = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "ReceiveRecovered", reason = "Tool returned later", lines = new[] { new { materialCode = "VT1", locationCode = location, quantity = 1, originalIssueLineId = line, condition = "Reusable", conditionNote = "Working normally" } } }));
        await Act(f, Code(back), "approve");
        await f.InDb(async db => Assert.Equal(2, (await db.Set<MaterialBalance>().SingleAsync()).Quantity));
    }
    [PostgreSqlFact]
    public async Task Multiple_generic_changes_without_materials_are_traceable_and_corrections_preserve_original()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var work = await Manual(f);
        object Body(string target, string? replaces = null) => new { requestId = Guid.NewGuid(),  deviceCode = "TB-ROLE-001", kind = "Inspect", target, beforeValue = "Before inspection", afterValue = "After inspection", reason = "Professional finding", replacesCode = replaces };
        var first = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", Body("Power supply")));
        var second = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", Body("Calibration")));
        Assert.NotEqual(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());
        await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", Body("Power supply corrected", first.GetProperty("code").GetString())));
        var history = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/changes?deviceCode=TB-ROLE-001"); Assert.Equal(3, history.GetProperty("totalCount").GetInt32());
        await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", Body("Later direct inspection")));
    }
    [PostgreSqlFact]
    public async Task Unit_precision_is_enforced_and_used_unit_cannot_change()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "Receipt", reference = "Source", reason = "Invalid fractional piece", lines = new[] { Line(location, 0.5m) } }), HttpStatusCode.BadRequest);
        await Expect(await f.Manager.PutAsJsonAsync(Root + "/units/CAI", new { name = "Piece", decimalPlaces = 2, status = "Active", version = 1 }), HttpStatusCode.Conflict);
    }
    [PostgreSqlFact]
    public async Task Concurrent_issues_cannot_overdraw_and_audit_failure_rolls_back_stock()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location, 5);
        var first = Code(await Doc(f, "Issue", [Line(location, 4)], target: "PB_TARGET")); var second = Code(await Doc(f, "Issue", [Line(location, 4)], target: "PB_TARGET"));
        var replies = await Task.WhenAll(new[] { first, second }.Select(code => f.Staff.PostAsJsonAsync(Root + "/documents/" + code + "/post", new { requestId = Guid.NewGuid(), version = 1 })));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.NoContent); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict); foreach (var r in replies) r.Dispose();
        var receipt = Code(await Doc(f, "Receipt", [Line(location, 2)])); await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_material_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Audit failure'; END $$; CREATE TRIGGER fail_material_audit BEFORE INSERT ON material_document_history FOR EACH ROW EXECUTE FUNCTION fail_material_audit();"));
        await Act(f, receipt, "post", expected: HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Equal(1, (await db.Set<MaterialBalance>().SingleAsync()).Quantity); Assert.Equal(2, await db.Set<MaterialLedgerEntry>().CountAsync()); });
    }
    [PostgreSqlFact]
    public async Task Recovered_reusable_parts_merge_stock_and_must_be_received_before_work_closes()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location, 5); var work = await Manual(f);
        var change = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", new { requestId = Guid.NewGuid(),  deviceCode = "TB-ROLE-001", kind = "Remove", target = "Replaceable part", beforeValue = "Installed", afterValue = "Removed", reason = "Equipment intervention", removed = new[] { new { materialCode = "VT1", description = "Reusable part", unit = "Piece", quantity = 1, reusable = true, conditionNote = "Tested working", disposition = "ReturnToStock" } } }));
        var removal = change.GetProperty("removed")[0].GetProperty("id").GetGuid(); var recovered = Code(await Doc(f, "ReceiveRecovered", [Line(location, 1, removed: removal)]));
        await Act(f, recovered, "post"); await f.InDb(async db => { Assert.Single(await db.Set<MaterialBalance>().ToListAsync()); Assert.Equal(6, (await db.Set<MaterialBalance>().SingleAsync()).Quantity); });
        var duplicate = Code(await Doc(f, "ReceiveRecovered", [Line(location, 1, removed: removal)])); await Act(f, duplicate, "post", expected: HttpStatusCode.Conflict);
    }
    [PostgreSqlFact]
    public async Task Stock_transfer_preserves_total_and_ledger_and_changes_are_immutable()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location, 5);
        var target = await Json(await f.Manager.PostAsJsonAsync(Root + "/locations", new { name = "Second bin", warehouseCode = "K1", status = "Active" }));
        var transfer = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "Transfer", reason = "Reorganize warehouse", lines = new[] { new { materialCode = "VT1", locationCode = location, toLocationCode = target.GetProperty("code").GetString(), quantity = 2, condition = "New" } } }));
        await Act(f, Code(transfer), "post");
        await f.InDb(async db => { Assert.Equal(5, await db.Set<MaterialBalance>().SumAsync(b => b.Quantity)); Assert.Equal(3, await db.Set<MaterialLedgerEntry>().CountAsync()); await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE material_ledger SET delta = delta + 1")); });
        var report = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/report"); Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
    }
    private static async Task<string> Source(EquipmentStaffWorkflowTests.Fixture f, bool mixed = false)
    {
        var lines = new List<object> { new { sourceLineCode = "M1", kind = "Material", description = "Replacement part", quantity = 3, unit = "CAI", specialization = "Common" } };
        if (mixed) lines.Add(new { sourceLineCode = "E1", kind = "Equipment", description = "Equipment", quantity = 1, unit = "CAI", categoryCode = "CAT_ROLE", specialization = "Common" });
        var result = await Json(await f.Manager.PostAsJsonAsync("/api/v1/procurement-requests/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "MATERIAL-SOURCE", sourceVersion = 1, action = "Create", request = new { requestType = "Purchase", title = "Materials for requesting department", requestingDepartmentCode = "PB_TARGET", sourceStatus = "Submitted", createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:01:00Z", lines } }));
        var code = result.GetProperty("displayCode").GetString()!; await SourceRequestAssignmentTests.Assign(f, code); return code;
    }
    private static async Task<string> SourceIssue(EquipmentStaffWorkflowTests.Fixture f, string source, string location)
    {
        var doc = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "Issue", reason = "Whole material allocation", requestCode = source, lines = new[] { new { materialCode = "VT1", locationCode = location, quantity = 3, condition = "New", sourceLineCode = "M1" } } }));
        var code = Code(doc); await Act(f, code, "post"); return code;
    }
    [PostgreSqlFact]
    public async Task Material_source_completes_after_issue_posting_and_rejects_late_pmms_cancellation()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location); var source = await Source(f); var code = await SourceIssue(f, source, location);
        var detail = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/procurement-requests/" + source); Assert.Contains(detail.GetProperty("documents").EnumerateArray(), d => d.GetProperty("kind").GetString() == "MaterialIssue");
        Assert.Equal("Completed", (await Get(f, code)).GetProperty("summary").GetProperty("status").GetString());
        var cancel = await Json(await f.Manager.PostAsJsonAsync("/api/v1/procurement-requests/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "MATERIAL-SOURCE", sourceVersion = 2, action = "Cancel", reason = "Late cancellation" })); Assert.Equal("CancelRejected", cancel.GetProperty("outcome").GetString());
        detail = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/procurement-requests/" + source);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/procurement-requests/" + source + "/complete", new { version = detail.GetProperty("summary").GetProperty("version").GetInt32() }));
    }
    [PostgreSqlFact]
    public async Task Legacy_posted_material_issues_complete_on_upgrade_without_reposting_or_completing_mixed_deliveries()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var location = await Catalog(f); await Stock(f, location);
        var ordinary = Code(await Doc(f, "Issue", [Line(location, 2)], target: "PB_TARGET"));
        await Act(f, ordinary, "post");
        var source = await Source(f, mixed: true);
        var mixed = await SourceIssue(f, source, location);
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261120074818_SourceRequestAssignments");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                ALTER TABLE material_documents DISABLE TRIGGER immutable_posted_material_document;
                UPDATE material_documents SET status = 'AwaitingReceipt' WHERE code = {ordinary};
                ALTER TABLE material_documents ENABLE TRIGGER immutable_posted_material_document;
                """);
            await migrator.MigrateAsync();
            var completed = await db.Set<MaterialDocument>().AsNoTracking().SingleAsync(d => d.Code == ordinary);
            Assert.Equal(MaterialDocumentStatus.Completed, completed.Status);
            Assert.Equal(3, completed.Version);
            Assert.Null(completed.ReceivedAt);
            Assert.Null(completed.ReceivedByAccountId);
            Assert.Equal(MaterialDocumentStatus.AwaitingReceipt, (await db.Set<MaterialDocument>().AsNoTracking().SingleAsync(d => d.Code == mixed)).Status);
            Assert.Equal(15, (await db.Set<MaterialBalance>().SingleAsync()).Quantity);
            Assert.Equal(3, await db.Set<MaterialLedgerEntry>().CountAsync());
            Assert.Single(await db.Set<MaterialDocumentHistory>().Where(h => h.DocumentId == completed.Id && h.Action == "RECEIPT_STEP_REMOVED").ToListAsync());
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE material_documents SET reason = 'Changed' WHERE code = {ordinary}"));
        });
    }
    [PostgreSqlFact]
    public async Task Mixed_equipment_and_material_delivery_cannot_be_completed_separately()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location); var source = await Source(f, mixed: true); var material = await SourceIssue(f, source, location);
        var transfer = await Json(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new { sourceRequestCode = source, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Whole equipment allocation", items = new[] { new { deviceCode = "TB-ROLE-001", sourceLineCode = "E1" } } }), HttpStatusCode.Created);
        var code = transfer.GetProperty("transferCode").GetString()!; await Expect(await f.Department.PostAsJsonAsync("/api/v1/device-transfers/" + code + "/confirm-receipt-intent", new { }));
        await Expect(await f.Department.PostAsJsonAsync("/api/v1/device-transfers/" + code + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }), HttpStatusCode.Conflict);
        await Act(f, material, "receive", f.Department, HttpStatusCode.BadRequest);
        var body = new { requestId = Guid.NewGuid(), version = 2, devices = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } };
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_combined_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Combined audit failure'; END $$; CREATE TRIGGER fail_combined_audit BEFORE INSERT ON material_document_history FOR EACH ROW EXECUTE FUNCTION fail_combined_audit();"));
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents/" + material + "/receive", body), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Equal(CSVC.Core.DeviceTransfers.DeviceTransferStatus.Approved, (await db.DeviceTransfers.SingleAsync()).Status); Assert.Equal(MaterialDocumentStatus.AwaitingReceipt, (await db.Set<MaterialDocument>().SingleAsync(d => d.Code == material)).Status); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_combined_audit ON material_document_history; DROP FUNCTION fail_combined_audit();"); });
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents/" + material + "/receive", body));
        await f.InDb(async db => { Assert.Equal(CSVC.Core.DeviceTransfers.DeviceTransferStatus.Completed, (await db.DeviceTransfers.SingleAsync()).Status); Assert.Equal(MaterialDocumentStatus.Completed, (await db.Set<MaterialDocument>().SingleAsync(d => d.Code == material)).Status); });
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents/" + material + "/receive", body));
    }
    [PostgreSqlFact]
    public async Task Service_result_requires_reconciled_materials_and_keeps_detailed_interventions()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); var location = await Catalog(f); await Stock(f, location);
        var order = await Json(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new { requestId = Guid.NewGuid(), orderType = "Maintenance", items = new[] { new { deviceCode = "TB-ROLE-001", executionMethod = "OnSite", workDescription = "General intervention" } } }), HttpStatusCode.Created);
        var code = order.GetProperty("displayCode").GetString()!; await Expect(await f.Manager.PostAsJsonAsync("/api/v1/service-orders/" + code + "/approve", new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32() }));
        order = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/service-orders/" + code);
        var doc = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "Issue", reason = "Service materials", serviceOrderCode = code, lines = new[] { Line(location, 2, device: "TB-ROLE-001") } }));
        await Act(f, Code(doc), "post");
        var result = new { requestId = Guid.NewGuid(), version = order.GetProperty("version").GetInt32(), items = new[] { new { itemNo = 1, result = "Completed", description = "Equipment repaired and checked" } } };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders/" + code + "/results", result), HttpStatusCode.Conflict);
        await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", new { requestId = Guid.NewGuid(),  deviceCode = "TB-ROLE-001", serviceOrderCode = code, kind = "Replace", target = "Generic replacement assembly", beforeValue = "Old assembly", afterValue = "New assembly", reason = "Replacement", uses = new[] { new { source = "Warehouse", issueLineId = doc.GetProperty("lines")[0].GetProperty("id").GetGuid(), quantity = 2 } } }));
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders/" + code + "/results", result));
        var history = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/changes?deviceCode=TB-ROLE-001");
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.Equal("Replace", history.GetProperty("items")[0].GetProperty("kind").GetString());
        await f.InDb(async db => Assert.Equal(1, await db.ServiceOrderHistories.CountAsync(h => h.ActionCode == "ITEM_COMPLETED")));
    }
}
