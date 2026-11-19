using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.Infrastructure.Persistence.Migrations;

// Only reconcile guards installed by the retired local/stash migration.
// Keep its history entry and all parent references; the pulled workflow remains authoritative.
[DbContext(typeof(CsvcDbContext))]
[Migration("20261120000100_ReconcileLegacyComponentGuards")]
public sealed class ReconcileLegacyComponentGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            DO $reconcile$
            BEGIN
                IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261112000000_RetireDeviceComponentRelationships') THEN
            DROP TRIGGER IF EXISTS tr_receipt_items_legacy_parent_readonly ON device_receipt_items;
            DROP FUNCTION IF EXISTS protect_legacy_receipt_parent();
            DROP TRIGGER IF EXISTS tr_devices_legacy_parent_readonly ON devices;
            DROP FUNCTION IF EXISTS protect_legacy_device_parent();
                END IF;
            END $reconcile$;
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            DO $restore$
            BEGIN
                IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261112000000_RetireDeviceComponentRelationships') THEN
            CREATE FUNCTION protect_legacy_device_parent() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.parent_device_id IS NOT NULL THEN
                        RAISE EXCEPTION 'Component relationships are retired'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_devices_legacy_parent_readonly';
                    END IF;
                ELSIF NEW.parent_device_id IS DISTINCT FROM OLD.parent_device_id THEN
                    RAISE EXCEPTION 'Legacy component relationships are read-only'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_devices_legacy_parent_readonly';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER tr_devices_legacy_parent_readonly
                BEFORE INSERT OR UPDATE OF parent_device_id ON devices
                FOR EACH ROW EXECUTE FUNCTION protect_legacy_device_parent();

            CREATE FUNCTION protect_legacy_receipt_parent() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.parent_device_code IS NOT NULL THEN
                        RAISE EXCEPTION 'Receipt component relationships are retired'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_receipt_items_legacy_parent_readonly';
                    END IF;
                ELSIF NEW.parent_device_code IS DISTINCT FROM OLD.parent_device_code THEN
                    RAISE EXCEPTION 'Legacy receipt component references are read-only'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_receipt_items_legacy_parent_readonly';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER tr_receipt_items_legacy_parent_readonly
                BEFORE INSERT OR UPDATE OF parent_device_code ON device_receipt_items
                FOR EACH ROW EXECUTE FUNCTION protect_legacy_receipt_parent();
                END IF;
            END $restore$;
            """);
}
