using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CsvcDbContext))]
[Migration("20261120010000_DevicePackagesReadOnlyReferences")]
public sealed class DevicePackagesReadOnlyReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            CREATE FUNCTION protect_package_device_reference() RETURNS trigger LANGUAGE plpgsql AS $$
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
            CREATE TRIGGER tr_devices_package_reference_readonly
                BEFORE INSERT OR UPDATE OF parent_device_id ON devices
                FOR EACH ROW EXECUTE FUNCTION protect_package_device_reference();

            CREATE FUNCTION protect_package_receipt_reference() RETURNS trigger LANGUAGE plpgsql AS $$
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
            CREATE TRIGGER tr_receipt_items_package_reference_readonly
                BEFORE INSERT OR UPDATE OF parent_device_code ON device_receipt_items
                FOR EACH ROW EXECUTE FUNCTION protect_package_receipt_reference();
            """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_receipt_items_package_reference_readonly ON device_receipt_items;
            DROP FUNCTION IF EXISTS protect_package_receipt_reference();
            DROP TRIGGER IF EXISTS tr_devices_package_reference_readonly ON devices;
            DROP FUNCTION IF EXISTS protect_package_device_reference();
            """);
}
