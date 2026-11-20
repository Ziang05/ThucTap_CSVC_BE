using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class DevicePackagePolicyTests
{
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }

    [PostgreSqlFact]
    public async Task Legacy_references_survive_upgrade_locks_and_individual_transfers_without_expansion()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261120000100_ReconcileLegacyComponentGuards");
            var parent = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001");
            var child = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002");
            child.ParentDeviceId = parent.Id; await db.SaveChangesAsync();
            var before = await db.Devices.OrderBy(d => d.DeviceCode).Select(d => new { d.Id, d.Barcode, d.ParentDeviceId, d.Quantity }).ToArrayAsync();
            await migrator.MigrateAsync();
            Assert.Equal(before, await db.Devices.OrderBy(d => d.DeviceCode).Select(d => new { d.Id, d.Barcode, d.ParentDeviceId, d.Quantity }).ToArrayAsync());
            var violation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE devices SET parent_device_id=NULL WHERE device_code='TB-ROLE-002'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        });
        await Expect(await f.Manager.PutAsJsonAsync("/api/v1/devices/TB-ROLE-001/lock", new { }));
        await f.InDb(async db => Assert.Equal(EntityStatus.Active, (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002")).Status));
        await Expect(await f.Manager.DeleteAsync("/api/v1/devices/TB-ROLE-001/lock"));
        using var created = await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new { sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Independent device", items = new[] { new { deviceCode = "TB-ROLE-001" } } });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var transfer = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, transfer.GetProperty("itemCount").GetInt32());
        var route = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null));
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }));
        await f.InDb(async db =>
        {
            var parent = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001"); var child = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002");
            Assert.NotEqual(parent.DepartmentId, child.DepartmentId); Assert.Equal(parent.Id, child.ParentDeviceId);
            Assert.Equal(1, child.Quantity); Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task New_receipts_reject_component_links_without_reserving_device_identities()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.InDb(async db => { db.Add(new Supplier { DisplayCode = "PACKAGE_SUP", Name = "Supplier" }); await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", new { requestingDepartmentCode = "PB_TARGET", supplierCode = "PACKAGE_SUP", deliveryDate = "2026-10-21", deliveryBatch = "1", items = new[] { new { deviceCode = "NEW-PACKAGE-CHILD", name = "Independent equipment", categoryCode = "CAT_ROLE", parentDeviceCode = "TB-ROLE-001" } } }), HttpStatusCode.Conflict);
        await f.InDb(async db => { Assert.False(await db.DeviceIdentities.AnyAsync(d => d.DeviceCode == "NEW-PACKAGE-CHILD")); Assert.Empty(await db.DeviceReceipts.ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Barcode_lookup_is_repeatable_scoped_and_never_changes_identity()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var device = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/TB-ROLE-001");
        var barcode = device.GetProperty("barcode").GetString()!;
        for (var i = 0; i < 2; i++)
        {
            var found = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/by-barcode/" + barcode.ToLowerInvariant());
            Assert.Equal("TB-ROLE-001", found.GetProperty("deviceCode").GetString()); Assert.Equal(barcode, found.GetProperty("barcode").GetString());
        }
        await Expect(await f.Department.GetAsync("/api/v1/devices/by-barcode/" + barcode), HttpStatusCode.NotFound);
        await Expect(await f.Staff.GetAsync("/api/v1/devices/by-barcode/BC" + new string('0', 30)), HttpStatusCode.NotFound);
        await Expect(await f.Staff.GetAsync("/api/v1/devices/by-barcode/INVALID"), HttpStatusCode.BadRequest);
        Assert.Equal(device.ToString(), (await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/devices/TB-ROLE-001")).ToString());
    }
}
