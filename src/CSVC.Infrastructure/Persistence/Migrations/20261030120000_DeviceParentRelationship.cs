using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CSVC.Infrastructure.Persistence;

namespace CSVC.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CsvcDbContext))]
[Migration("20261030120000_DeviceParentRelationship")]
public sealed class DeviceParentRelationship : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "parent_device_id", table: "devices", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "parent_device_code", table: "device_receipt_items", type: "varchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.CreateIndex(name: "ix_devices_parent_device_id", table: "devices", column: "parent_device_id");
        migrationBuilder.AddCheckConstraint(name: "ck_devices_parent_not_self", table: "devices", sql: "parent_device_id IS NULL OR parent_device_id <> id");
        migrationBuilder.AddForeignKey(name: "fk_devices_devices_parent_device_id", table: "devices", column: "parent_device_id", principalTable: "devices", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_devices_devices_parent_device_id", table: "devices");
        migrationBuilder.DropCheckConstraint(name: "ck_devices_parent_not_self", table: "devices");
        migrationBuilder.DropIndex(name: "ix_devices_parent_device_id", table: "devices");
        migrationBuilder.DropColumn(name: "parent_device_id", table: "devices");
        migrationBuilder.DropColumn(name: "parent_device_code", table: "device_receipt_items");
    }
}
