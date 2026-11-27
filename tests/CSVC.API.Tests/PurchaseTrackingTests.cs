using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Devices;
using CSVC.Core.ProcurementRequests;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class PurchaseTrackingTests
{
    private const string Root = "/api/v1/procurement-requests";
    private static Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
        => SourceRequestAssignmentTests.Read(response, status);
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    private static async Task<string> Create(EquipmentStaffWorkflowTests.Fixture f, params (string Code, int Quantity, string? Category)[] lines)
    {
        var created = await Read(await f.Manager.PostAsJsonAsync(Root + "/simulated-events", new
        {
            eventId = Guid.NewGuid(), externalCode = "BUY-" + Guid.NewGuid().ToString("N"), sourceVersion = 1, action = "Create",
            request = new { requestType = "Purchase", title = "Equipment purchase", requestingDepartmentCode = "PB_TARGET", sourceStatus = "Submitted",
                createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:00:00Z",
                lines = lines.Select(line => new { sourceLineCode = line.Code, kind = "Equipment", description = line.Code,
                    quantity = line.Quantity, unit = "Cai", categoryCode = line.Category }) }
        }));
        var code = created.GetProperty("displayCode").GetString()!;
        await SourceRequestAssignmentTests.Assign(f, code);
        return code;
    }
    internal static async Task Track(EquipmentStaffWorkflowTests.Fixture f, string code)
        => await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/track-purchase",
            new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, code) }));
    private static object Allocation(string code, params (string Line, string Device)[] items)
        => new { sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET",
            targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Complete allocation",
            items = items.Select(item => new { sourceLineCode = item.Line, deviceCode = item.Device }) };

    [PostgreSqlFact]
    public async Task Stock_does_not_double_count_categories_or_unrestricted_lines()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 1, "CAT_ROLE"), ("L2", 2, "CAT_ROLE"));
        var stock = await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"));
        Assert.False(stock.GetProperty("canAllocate").GetBoolean());
        var rows = stock.GetProperty("lines");
        Assert.Equal(1, rows[0].GetProperty("availableQuantity").GetInt32());
        Assert.Equal(1, rows[1].GetProperty("availableQuantity").GetInt32());
        Assert.Equal(1, rows[1].GetProperty("missingQuantity").GetInt32());
        var unrestricted = await Create(f, ("ANY", 1, null), ("SPECIFIC", 2, "CAT_ROLE"));
        stock = await Read(await f.Staff.GetAsync(Root + "/" + unrestricted + "/stock"));
        Assert.False(stock.GetProperty("canAllocate").GetBoolean());
        Assert.Equal(1, stock.GetProperty("lines")[0].GetProperty("missingQuantity").GetInt32());
        await Expect(await f.Department.GetAsync(Root + "/" + code + "/stock"), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.GetAsync(Root + "/" + code + "/stock"), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Stock_excludes_in_use_damaged_reserved_and_other_department_devices()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 1, "CAT_ROLE"));
        await f.InDb(async db => {
            (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001")).UseState = DeviceUseState.InUse;
            (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002")).Condition = DeviceCondition.Damaged;
            await db.SaveChangesAsync();
        });
        var stock = await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"));
        Assert.Equal(0, stock.GetProperty("lines")[0].GetProperty("availableQuantity").GetInt32());
        await f.InDb(async db => {
            var device = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002"); device.Condition = DeviceCondition.Good;
            db.Add(new DeviceOperationHold { DeviceCode = device.DeviceCode, DeviceId = device.Id,
                OperationKind = DeviceOperationKind.Loan, OperationId = Guid.NewGuid(), HeldAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        stock = await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"));
        Assert.Equal(0, stock.GetProperty("lines")[0].GetProperty("availableQuantity").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Tracking_is_assignee_only_versioned_idempotent_and_audited_without_creating_receipts()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 3, "CAT_ROLE")); var route = Root + "/" + code;
        var command = new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, code) };
        await Expect(await f.Manager.PostAsJsonAsync(route + "/track-purchase", command), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/track-purchase", command), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/track-purchase", new { requestId = Guid.NewGuid(), version = command.version - 1 }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/track-purchase", command));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/track-purchase", command));
        var detail = await Read(await f.Staff.GetAsync(route));
        Assert.Equal("TrackingPurchase", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal(command.version + 1, detail.GetProperty("summary").GetProperty("version").GetInt32());
        var list = await Read(await f.Staff.GetAsync(Root + "?status=TrackingPurchase"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        await Expect(await f.Staff.PostAsJsonAsync(route + "/status", new { version = command.version + 1, status = "InProgress" }), HttpStatusCode.Conflict);
        await f.InDb(async db => {
            Assert.Single(await db.Set<ProcurementRequestEvent>().Where(e => e.Action == "PURCHASE_TRACKING_STARTED").ToArrayAsync());
            Assert.Empty(await db.DeviceReceipts.ToArrayAsync());
            Assert.Empty(await db.DeviceTransfers.ToArrayAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Sufficient_stock_uses_full_allocation_and_rejects_purchase_tracking_and_partial_allocation()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 2, "CAT_ROLE"));
        var stock = await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"));
        Assert.True(stock.GetProperty("canAllocate").GetBoolean());
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/track-purchase",
            new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, code) }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"))), HttpStatusCode.BadRequest);
        await f.InDb(async db => { Assert.Empty(await db.DeviceTransfers.ToArrayAsync()); Assert.Empty(await db.DeviceTransferItems.ToArrayAsync()); });
        await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "TB-ROLE-002"))), HttpStatusCode.Created);
    }

    [PostgreSqlFact]
    public async Task Allocation_rechecks_actual_device_state_after_the_stock_check()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 2, "CAT_ROLE"));
        Assert.True((await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"))).GetProperty("canAllocate").GetBoolean());
        await f.InDb(async db => { (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002")).UseState = DeviceUseState.InUse; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "TB-ROLE-002"))), HttpStatusCode.BadRequest);
        await f.InDb(async db => Assert.Empty(await db.DeviceTransfers.ToArrayAsync()));
        await f.InDb(async db => { (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002")).UseState = DeviceUseState.InStorage; await db.SaveChangesAsync(); });
        var transfer = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "TB-ROLE-002"))), HttpStatusCode.Created);
        Assert.Equal("Approved", transfer.GetProperty("status").GetString());
        await f.InDb(async db => Assert.Equal(2, await db.DeviceOperationHolds.CountAsync()));
        var detail = await Read(await f.Staff.GetAsync(Root + "/" + code));
        Assert.Equal("AwaitingReceipt", detail.GetProperty("summary").GetProperty("status").GetString());
    }

    [PostgreSqlFact]
    public async Task Tracked_purchase_receives_goods_then_allocates_every_requested_device_before_closing()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 3, "CAT_ROLE"));
        await Read(await f.Manager.PostAsJsonAsync("/api/v1/suppliers", new { displayCode = "BUY_SUPPLIER", name = "Supplier" }), HttpStatusCode.Created);
        var receiptInput = new { sourceRequestCode = code, supplierCode = "BUY_SUPPLIER", carrierName = "Carrier",
            requestingDepartmentCode = "PB_TARGET", requestCode = "EXTERNAL-ORDER", deliveryDate = "2026-10-21", deliveryBatch = "1", paidInvoiceCode = "INV-1", notes = "Delivered equipment",
            items = new[] { new { sourceLineCode = "L1", deviceCode = "BUY-NEW", categoryCode = "CAT_ROLE", name = "Purchased device" } } };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", receiptInput), HttpStatusCode.Conflict);
        await Track(f, code);
        var receipt = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", receiptInput), HttpStatusCode.Created);
        var receiptRoute = "/api/v1/device-receipts/" + receipt.GetProperty("receiptCode").GetString();
        Assert.Equal("TrackingPurchase", (await Read(await f.Staff.GetAsync(Root + "/" + code))).GetProperty("summary").GetProperty("status").GetString());
        await Expect(await f.Manager.PostAsync(receiptRoute + "/approve", null));
        await Expect(await f.Staff.PostAsync(receiptRoute + "/complete", null));
        Assert.Equal("AwaitingHandover", (await Read(await f.Staff.GetAsync(Root + "/" + code))).GetProperty("summary").GetProperty("status").GetString());
        var stock = await Read(await f.Staff.GetAsync(Root + "/" + code + "/stock"));
        Assert.True(stock.GetProperty("canAllocate").GetBoolean());
        Assert.Equal(3, stock.GetProperty("lines")[0].GetProperty("availableQuantity").GetInt32());
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "BUY-NEW"))), HttpStatusCode.BadRequest);
        var transfer = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "TB-ROLE-002"), ("L1", "BUY-NEW"))), HttpStatusCode.Created);
        Assert.Equal("Approved", transfer.GetProperty("status").GetString());
        Assert.Equal("AwaitingReceipt", (await Read(await f.Staff.GetAsync(Root + "/" + code))).GetProperty("summary").GetProperty("status").GetString());
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/receive", new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, code), receivedInFull = true,
            items = new[] { "TB-ROLE-001", "TB-ROLE-002", "BUY-NEW" }.Select(deviceCode => new { deviceCode, condition = "Good" }) }));
        var finished = await Read(await f.Staff.GetAsync(Root + "/" + code));
        Assert.Equal("Completed", finished.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Success", finished.GetProperty("result").GetString());
    }

    [PostgreSqlFact]
    public async Task Existing_stock_moves_directly_to_physical_receipt_and_only_actual_receiver_can_complete_it()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 2, "CAT_ROLE")); var route = Root + "/" + code;
        Assert.Equal("Received", (await Read(await f.Staff.GetAsync(route))).GetProperty("summary").GetProperty("status").GetString());
        await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"), ("L1", "TB-ROLE-002"))), HttpStatusCode.Created);
        var version = await SourceRequestAssignmentTests.Version(f, code);
        var detail = await Read(await f.Department.GetAsync(route));
        Assert.Equal("AwaitingReceipt", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.True(detail.GetProperty("canConfirmReceipt").GetBoolean());
        Assert.False((await Read(await f.Staff.GetAsync(route))).GetProperty("canConfirmReceipt").GetBoolean());
        object Receipt(bool received, params string[] codes) => new { requestId = Guid.NewGuid(), version, receivedInFull = received,
            items = codes.Select(deviceCode => new { deviceCode, condition = "Good" }) };
        var complete = Receipt(true, "TB-ROLE-001", "TB-ROLE-002");
        await Expect(await f.Staff.PostAsJsonAsync(route + "/receive", complete), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", Receipt(false, "TB-ROLE-001", "TB-ROLE-002")), HttpStatusCode.BadRequest);
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", Receipt(true, "TB-ROLE-001")), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version }), HttpStatusCode.Conflict);
        await f.InDb(async db => {
            Assert.All(await db.Devices.Where(device => device.DeviceCode != "TB-ROLE-003").ToArrayAsync(), device => Assert.Equal(DeviceUseState.InStorage, device.UseState));
            Assert.Equal(2, await db.DeviceOperationHolds.CountAsync());
        });
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", complete));
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", complete));
        detail = await Read(await f.Department.GetAsync(route));
        Assert.Equal("Completed", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canConfirmReceipt").GetBoolean());
        await f.InDb(async db => {
            Assert.Empty(await db.DeviceOperationHolds.ToArrayAsync());
            Assert.Single(await db.Set<ProcurementRequestEvent>().Where(entry => entry.Action == "PHYSICAL_RECEIPT_CONFIRMED").ToArrayAsync());
            Assert.Single(await db.Set<ProcurementRequestEvent>().Where(entry => entry.Action == "REQUEST_COMPLETED").ToArrayAsync());
            var target = (await db.Departments.SingleAsync(department => department.Code == "PB_TARGET")).Id;
            Assert.All(await db.Devices.Where(device => device.DeviceCode != "TB-ROLE-003").ToArrayAsync(), device => { Assert.Equal(target, device.DepartmentId); Assert.Equal(DeviceUseState.InUse, device.UseState); });
        });
    }

    [PostgreSqlFact]
    public async Task Physical_receipt_and_automatic_request_completion_roll_back_if_confirmation_audit_fails()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        var code = await Create(f, ("L1", 1, "CAT_ROLE")); var route = Root + "/" + code;
        await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", Allocation(code, ("L1", "TB-ROLE-001"))), HttpStatusCode.Created);
        var body = new { requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, code), receivedInFull = true,
            items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } };
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_physical_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action = 'PHYSICAL_RECEIPT_CONFIRMED' THEN RAISE EXCEPTION 'Test receipt audit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_physical_receipt BEFORE INSERT ON procurement_request_events FOR EACH ROW EXECUTE FUNCTION reject_physical_receipt();
            """));
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", body), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => {
            Assert.Equal(ProcurementRequestStatus.AwaitingReceipt, (await db.Set<ProcurementRequest>().SingleAsync()).Status);
            Assert.Equal(DeviceUseState.InStorage, (await db.Devices.SingleAsync(device => device.DeviceCode == "TB-ROLE-001")).UseState);
            Assert.Single(await db.DeviceOperationHolds.ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_physical_receipt ON procurement_request_events; DROP FUNCTION reject_physical_receipt();");
        });
        await Expect(await f.Department.PostAsJsonAsync(route + "/receive", body));
        Assert.Equal("Completed", (await Read(await f.Department.GetAsync(route))).GetProperty("summary").GetProperty("status").GetString());
    }
}
