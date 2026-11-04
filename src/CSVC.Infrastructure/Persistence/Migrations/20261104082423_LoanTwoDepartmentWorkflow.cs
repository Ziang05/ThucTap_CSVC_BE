using System;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoanTwoDepartmentWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Commit the new enum label before subsequent commands can use it. Reapplication after Down is safe.
            migrationBuilder.Sql("ALTER TYPE device_loan_status ADD VALUE IF NOT EXISTS 'PENDING_RETURN_CONFIRMATION'", suppressTransaction: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lender_department_id",
                table: "device_loans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_requested_at",
                table: "device_loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_requested_by_account_id",
                table: "device_loans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workflow_version",
                table: "device_loans",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DeviceCondition>(
                name: "declared_return_condition",
                table: "device_loan_items",
                type: "device_condition",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "declared_return_note",
                table: "device_loan_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_loans_lender_department_id_status",
                table: "device_loans",
                columns: new[] { "lender_department_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_device_loans_return_requested_by_account_id",
                table: "device_loans",
                column: "return_requested_by_account_id");

            // Existing rows have no historical lender. Preserve their decisions and audit as legacy.
            migrationBuilder.Sql("UPDATE device_loans SET workflow_version = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_loans_two_parties",
                table: "device_loans",
                sql: "workflow_version IN (1,2) AND (workflow_version = 1 OR (lender_department_id IS NOT NULL AND lender_department_id <> borrower_department_id))");

            migrationBuilder.AddForeignKey(
                name: "FK_device_loans_accounts_return_requested_by_account_id",
                table: "device_loans",
                column: "return_requested_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_device_loans_departments_lender_department_id",
                table: "device_loans",
                column: "lender_department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM device_loans WHERE workflow_version = 2) THEN
                        RAISE EXCEPTION 'Cannot downgrade loans with two-party workflow data. Restore a reviewed backup instead.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_device_loans_accounts_return_requested_by_account_id",
                table: "device_loans");

            migrationBuilder.DropForeignKey(
                name: "FK_device_loans_departments_lender_department_id",
                table: "device_loans");

            migrationBuilder.DropIndex(
                name: "IX_device_loans_lender_department_id_status",
                table: "device_loans");

            migrationBuilder.DropIndex(
                name: "IX_device_loans_return_requested_by_account_id",
                table: "device_loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_loans_two_parties",
                table: "device_loans");

            migrationBuilder.DropColumn(
                name: "lender_department_id",
                table: "device_loans");

            migrationBuilder.DropColumn(
                name: "return_requested_at",
                table: "device_loans");

            migrationBuilder.DropColumn(
                name: "return_requested_by_account_id",
                table: "device_loans");

            migrationBuilder.DropColumn(
                name: "workflow_version",
                table: "device_loans");

            migrationBuilder.DropColumn(
                name: "declared_return_condition",
                table: "device_loan_items");

            migrationBuilder.DropColumn(
                name: "declared_return_note",
                table: "device_loan_items");

            // PostgreSQL enum labels cannot be removed safely. Retain the unused label on downgrade.
        }
    }
}
