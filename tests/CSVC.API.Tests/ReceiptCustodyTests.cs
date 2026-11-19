using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Devices;
using CSVC.Core.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class ReceiptCustodyTests
{
    private const string Receipts = "/api/v1/device-receipts";
    private static object Receipt(string requester, params object[] items) => new
    {
        supplierCode = "SUP_CUSTODY", requestingDepartmentCode = requester, deliveryDate = "2026-10-21", deliveryBatch = "1", items
    };
    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    {
        using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
    }
    private static async Task AddSupplier(EquipmentStaffWorkflowTests.Fixture f)
        => await f.InDb(async db => { db.Add(new Supplier { DisplayCode = "SUP_CUSTODY", Name = "Custody supplier" }); await db.SaveChangesAsync(); });

    [PostgreSqlFact]
    public async Task Requester_only_receives_ownership_after_separate_allocation_and_actual_receipt()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await AddSupplier(f);
        // A predeclared device's department is not evidence that it was physically received.
        var declared = await Created(await f.Staff.PostAsJsonAsync("/api/v1/devices", new
        {
            deviceCode = "CUSTODY-EXISTING", name = "Predeclared", categoryCode = "CAT_ROLE", departmentCode = "PB_TARGET",
            responsibleAccountCode = "TK_DEPARTMENT", serialNumber = "SN-CUSTODY"
        }));
        var receipt = await Created(await f.Staff.PostAsJsonAsync(Receipts, Receipt("PB_TARGET",
            new { existingDeviceCode = "CUSTODY-EXISTING" }, new { deviceCode = "CUSTODY-NEW", name = "New package", categoryCode = "CAT_ROLE" })));
        Assert.Equal(2, receipt.GetProperty("workflowVersion").GetInt32());
        Assert.Equal("PB_TARGET", receipt.GetProperty("requestingDepartmentCode").GetString());
        Assert.Equal("PB_TEST", receipt.GetProperty("receivingDepartmentCode").GetString());
        var route = Receipts + "/" + receipt.GetProperty("receiptCode").GetString();
        await Expect(await f.Department.PostAsync(route + "/complete", null), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }));
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/devices/CUSTODY-EXISTING/put-into-use", new { responsibleAccountCode = "TK_STAFF" }), HttpStatusCode.Conflict);
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_custody_receipt_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action_code='RECEIPT_COMPLETED' THEN RAISE EXCEPTION 'Test audit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_custody_receipt_audit BEFORE INSERT ON device_receipt_history FOR EACH ROW EXECUTE FUNCTION fail_custody_receipt_audit();
            """));
        await Expect(await f.Staff.PostAsync(route + "/complete", null), HttpStatusCode.ServiceUnavailable);
        var rolledBack = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/CUSTODY-EXISTING");
        Assert.Equal(0, rolledBack.GetProperty("quantity").GetInt32());
        Assert.Equal("PB_TARGET", rolledBack.GetProperty("departmentCode").GetString());
        Assert.Equal("TK_DEPARTMENT", rolledBack.GetProperty("responsibleAccountCode").GetString());
        await f.InDb(async db =>
        {
            Assert.Equal(2, await db.DeviceOperationHolds.CountAsync());
            Assert.False(await db.Devices.AnyAsync(d => d.DeviceCode == "CUSTODY-NEW"));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_custody_receipt_audit ON device_receipt_history; DROP FUNCTION fail_custody_receipt_audit()");
        });
        await Expect(await f.Staff.PostAsync(route + "/complete", null));
        foreach (var code in new[] { "CUSTODY-EXISTING", "CUSTODY-NEW" })
        {
            var device = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/" + code);
            Assert.Equal("PB_TEST", device.GetProperty("departmentCode").GetString());
            Assert.Equal("InStorage", device.GetProperty("useState").GetString());
            Assert.Equal("TK_STAFF", device.GetProperty("responsibleAccountCode").GetString());
            Assert.Equal(1, device.GetProperty("quantity").GetInt32());
            using var forbidden = await f.Department.GetAsync("/api/v1/devices/" + code);
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        }
        var received = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/CUSTODY-EXISTING");
        Assert.Equal(declared.GetProperty("barcode").GetString(), received.GetProperty("barcode").GetString());
        Assert.Equal("SN-CUSTODY", received.GetProperty("serialNumber").GetString());
        var stored = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices?useState=InStorage");
        Assert.Equal(2, stored.GetProperty("totalCount").GetInt32());
        var transfer = await Created(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new
        {
            sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT",
            reason = "Allocation after receipt", items = new[] { new { deviceCode = "CUSTODY-EXISTING" }, new { deviceCode = "CUSTODY-NEW" } }
        }));
        var transferRoute = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsync(transferRoute + "/confirm-receipt-intent", null));
        var waiting = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/CUSTODY-EXISTING");
        Assert.Equal("PB_TEST", waiting.GetProperty("departmentCode").GetString());
        Assert.Equal("InStorage", waiting.GetProperty("useState").GetString());
        await Expect(await f.Department.PostAsJsonAsync(transferRoute + "/handover-complete", new
        {
            items = new[] { new { deviceCode = "CUSTODY-EXISTING", condition = "Good" }, new { deviceCode = "CUSTODY-NEW", condition = "Good" } }
        }));
        var allocated = await f.Department.GetFromJsonAsync<JsonElement>("/api/v1/devices/CUSTODY-EXISTING");
        Assert.Equal("PB_TARGET", allocated.GetProperty("departmentCode").GetString());
        Assert.Equal("InUse", allocated.GetProperty("useState").GetString());
        Assert.Equal(1, allocated.GetProperty("quantity").GetInt32());
        await f.InDb(async db =>
        {
            Assert.Equal(2, await db.DeviceHistories.CountAsync(h => h.ActionCode.StartsWith("DEVICE_RECEIVED_")));
            Assert.Equal(2, await db.DeviceHistories.CountAsync(h => h.ActionCode.StartsWith("DEVICE_TRANSFER_COMPLETED_")));
            Assert.Equal("PB_TARGET", (await db.Departments.SingleAsync(d => d.Id == (db.DeviceReceipts.Single().RequestingDepartmentId))).Code);
        });
    }

    [PostgreSqlFact]
    public async Task Internal_use_requires_actual_stock_and_records_a_separate_event_without_same_department_transfer()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await AddSupplier(f);
        var receipt = await Created(await f.Staff.PostAsJsonAsync(Receipts, Receipt("PB_TEST",
            new { deviceCode = "INTERNAL-USE", name = "Office device", categoryCode = "CAT_ROLE" })));
        var route = Receipts + "/" + receipt.GetProperty("receiptCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }));
        await Expect(await f.Staff.PostAsync(route + "/complete", null));
        const string useRoute = "/api/v1/devices/INTERNAL-USE/put-into-use";
        await Expect(await f.Manager.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_STAFF" }), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_STAFF" }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_DEPARTMENT" }), HttpStatusCode.BadRequest);
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_internal_use_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action_code='DEVICE_PUT_INTO_USE' THEN RAISE EXCEPTION 'Test audit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_internal_use_audit BEFORE INSERT ON device_history FOR EACH ROW EXECUTE FUNCTION fail_internal_use_audit();
            """));
        await Expect(await f.Staff.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_MANAGER" }), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db =>
        {
            Assert.Equal(DeviceUseState.InStorage, (await db.Devices.SingleAsync(d => d.DeviceCode == "INTERNAL-USE")).UseState);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_internal_use_audit ON device_history; DROP FUNCTION fail_internal_use_audit()");
        });
        await Expect(await f.Staff.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_MANAGER", notes = "Used in equipment office" }));
        await Expect(await f.Staff.PostAsJsonAsync(useRoute, new { responsibleAccountCode = "TK_MANAGER" }), HttpStatusCode.Conflict);
        var device = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/INTERNAL-USE");
        Assert.Equal("PB_TEST", device.GetProperty("departmentCode").GetString());
        Assert.Equal("InUse", device.GetProperty("useState").GetString());
        Assert.Equal("TK_MANAGER", device.GetProperty("responsibleAccountCode").GetString());
        await f.InDb(async db =>
        {
            Assert.Empty(await db.DeviceTransfers.ToListAsync());
            Assert.Single(await db.DeviceHistories.Where(h => h.ActionCode == "DEVICE_PUT_INTO_USE").ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Receiving_requires_configuration_and_membership_and_prevents_changing_QTTB_with_incoming_work_or_stock()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await AddSupplier(f);
        var body = Receipt("PB_TARGET", new { deviceCode = "CONFIG-STOCK", name = "Stored package", categoryCode = "CAT_ROLE" });
        Guid equipmentId = default;
        await f.InDb(async db =>
        {
            var config = await db.EquipmentSettings.SingleAsync();
            equipmentId = config.EquipmentDepartmentId!.Value;
            config.EquipmentDepartmentId = null;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, body), HttpStatusCode.Conflict);
        await f.InDb(async db =>
        {
            (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = (await db.Departments.SingleAsync(d => d.Code == "PB_TARGET")).Id;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, body), HttpStatusCode.Forbidden);
        await f.InDb(async db => { (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = equipmentId; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, new
        {
            supplierCode = "SUP_CUSTODY", requestingDepartmentCode = "PB_TARGET", receivingDepartmentCode = "PB_TARGET", workflowVersion = 1,
            deliveryDate = "2026-10-21", deliveryBatch = "1", items = new[] { new { name = "Spoofed", categoryCode = "CAT_ROLE" } }
        }), HttpStatusCode.BadRequest);
        var created = await Created(await f.Staff.PostAsJsonAsync(Receipts, body));
        async Task ChangeSettings(HttpStatusCode expected)
        {
            var settings = await f.Manager.GetFromJsonAsync<JsonElement>("/api/v1/settings/equipment-department");
            await Expect(await f.Manager.PutAsJsonAsync("/api/v1/settings/equipment-department", new { departmentCode = "PB_TARGET", version = settings.GetProperty("version").GetInt32() }), expected);
        }
        await ChangeSettings(HttpStatusCode.Conflict);
        var route = Receipts + "/" + created.GetProperty("receiptCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }));
        await Expect(await f.Staff.PostAsync(route + "/complete", null));
        await ChangeSettings(HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/devices/CONFIG-STOCK/put-into-use", new { responsibleAccountCode = "TK_STAFF" }));
        await ChangeSettings(HttpStatusCode.NoContent);
    }

    [PostgreSqlFact]
    public async Task Migration_keeps_old_receipts_and_devices_unchanged_and_old_open_receipt_keeps_its_destination()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await AddSupplier(f);
        var receipt = await Created(await f.Staff.PostAsJsonAsync(Receipts, Receipt("PB_TARGET",
            new { deviceCode = "LEGACY-CUSTODY", name = "Old pending package", categoryCode = "CAT_ROLE" })));
        var code = receipt.GetProperty("receiptCode").GetString();
        await f.InDb(async db =>
        {
            var departments = await db.Devices.Select(d => new { d.Id, d.DepartmentId, d.Quantity, d.Barcode }).OrderBy(d => d.Id).ToListAsync();
            var historyCount = await db.DeviceReceiptHistories.CountAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261105032457_EquipmentInitiatedRecovery");
            await migrator.MigrateAsync();
            Assert.Equal(departments, await db.Devices.Select(d => new { d.Id, d.DepartmentId, d.Quantity, d.Barcode }).OrderBy(d => d.Id).ToListAsync());
            Assert.Equal(historyCount, await db.DeviceReceiptHistories.CountAsync());
            Assert.All(await db.Devices.ToListAsync(), d => Assert.Equal(DeviceUseState.Unspecified, d.UseState));
        });
        var legacy = await f.Staff.GetFromJsonAsync<JsonElement>(Receipts + "/" + code);
        Assert.Equal(1, legacy.GetProperty("workflowVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("receivingDepartmentCode").ValueKind);
        await Expect(await f.Manager.PostAsJsonAsync(Receipts + "/" + code + "/approve", new { }));
        await Expect(await f.Staff.PostAsync(Receipts + "/" + code + "/complete", null));
        var device = await f.Department.GetFromJsonAsync<JsonElement>("/api/v1/devices/LEGACY-CUSTODY");
        Assert.Equal("PB_TARGET", device.GetProperty("departmentCode").GetString());
        Assert.Equal("Unspecified", device.GetProperty("useState").GetString());
    }
}
