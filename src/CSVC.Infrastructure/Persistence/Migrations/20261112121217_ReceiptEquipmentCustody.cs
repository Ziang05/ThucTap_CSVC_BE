using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptEquipmentCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "use_state",
                table: "devices",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<Guid>(
                name: "receiving_department_id",
                table: "device_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workflow_version",
                table: "device_receipts",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // Preserve the meaning of every pre-existing receipt, including open ones.
            migrationBuilder.Sql("UPDATE device_receipts SET workflow_version = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_devices_use_state",
                table: "devices",
                sql: "use_state IN ('Unspecified', 'InStorage', 'InUse') AND (use_state = 'Unspecified' OR quantity = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_device_receipts_receiving_department_id",
                table: "device_receipts",
                column: "receiving_department_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipts_receiving_department",
                table: "device_receipts",
                sql: "(workflow_version = 1 AND receiving_department_id IS NULL) OR (workflow_version = 2 AND receiving_department_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_device_receipts_departments_receiving_department_id",
                table: "device_receipts",
                column: "receiving_department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_receipts_departments_receiving_department_id",
                table: "device_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_devices_use_state",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_device_receipts_receiving_department_id",
                table: "device_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipts_receiving_department",
                table: "device_receipts");

            migrationBuilder.DropColumn(
                name: "use_state",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "receiving_department_id",
                table: "device_receipts");

            migrationBuilder.DropColumn(
                name: "workflow_version",
                table: "device_receipts");
        }
    }
}
