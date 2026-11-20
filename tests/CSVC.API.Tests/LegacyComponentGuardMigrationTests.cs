using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class LegacyComponentGuardMigrationTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrade_retires_parent_writes_and_preserves_identity_on_fresh_and_legacy_databases(bool legacy)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            if (legacy)
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ('20261112000000_RetireDeviceComponentRelationships', '10.0.13');
                    """);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261119163934_AlignProcurementCatalogReferences");
            var guards = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_trigger WHERE NOT tgisinternal
                AND tgname IN ('tr_devices_legacy_parent_readonly','tr_receipt_items_legacy_parent_readonly')
                """).SingleAsync();
            Assert.Equal(legacy ? 2 : 0, guards);
            if (legacy)
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
                    UPDATE devices SET parent_device_id=(SELECT id FROM devices WHERE device_code='TB-ROLE-001')
                    WHERE device_code='TB-ROLE-002';
                    """));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
                Assert.Equal("ck_devices_legacy_parent_readonly", error.ConstraintName);
            }
            var identities = await db.Devices.OrderBy(d => d.DeviceCode).Select(d => new { d.Id, d.DeviceCode, d.Barcode }).ToListAsync();
            await migrator.MigrateAsync();
            Assert.Equal(identities, await db.Devices.OrderBy(d => d.DeviceCode).Select(d => new { d.Id, d.DeviceCode, d.Barcode }).ToListAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_trigger WHERE NOT tgisinternal
                AND tgname IN ('tr_devices_legacy_parent_readonly','tr_receipt_items_legacy_parent_readonly')
                """).SingleAsync());
            Assert.Equal(legacy ? 1 : 0, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM "__EFMigrationsHistory"
                WHERE "MigrationId"='20261112000000_RetireDeviceComponentRelationships'
                """).SingleAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
        using var linked = await f.Staff.PutAsJsonAsync("/api/v1/devices/TB-ROLE-002/parent", new { parentDeviceCode = "TB-ROLE-001" });
        Assert.True(linked.StatusCode == HttpStatusCode.Conflict, await linked.Content.ReadAsStringAsync());
        await f.InDb(async db =>
        {
            var parent = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001");
            var child = await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-002");
            Assert.Null(child.ParentDeviceId);
        });
    }
}
