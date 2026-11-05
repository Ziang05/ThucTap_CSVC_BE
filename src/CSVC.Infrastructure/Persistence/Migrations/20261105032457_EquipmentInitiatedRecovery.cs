using System;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentInitiatedRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_device_transfers_creator_account_id",
                table: "device_transfers");

            migrationBuilder.AddColumn<string>(
                name: "create_request_hash",
                table: "device_transfers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "create_request_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initiation_type",
                table: "device_transfers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SourceDepartment");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "source_accepted_at",
                table: "device_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_accepted_by_account_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "source_handover_confirmed_at",
                table: "device_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_handover_confirmed_by_account_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "source_rejected_at",
                table: "device_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_rejected_by_account_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_rejection_reason",
                table: "device_transfers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DeviceCondition>(
                name: "condition_at_source_handover",
                table: "device_transfer_items",
                type: "device_condition",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_handover_note",
                table: "device_transfer_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_creator_account_id_create_request_id",
                table: "device_transfers",
                columns: new[] { "creator_account_id", "create_request_id" },
                unique: true,
                filter: "create_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_source_accepted_by_account_id",
                table: "device_transfers",
                column: "source_accepted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_source_handover_confirmed_by_account_id",
                table: "device_transfers",
                column: "source_handover_confirmed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_source_rejected_by_account_id",
                table: "device_transfers",
                column: "source_rejected_by_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_equipment_recovery_workflow",
                table: "device_transfers",
                sql: "initiation_type IN ('SourceDepartment','EquipmentDepartment') AND\r\n(initiation_type = 'SourceDepartment' OR (\r\n    transfer_type = 'Recovery' AND create_request_id IS NOT NULL AND create_request_id <> '00000000-0000-0000-0000-000000000000'::uuid\r\n    AND create_request_hash IS NOT NULL AND create_request_hash ~ '^[0-9A-F]{64}$'\r\n    AND receiver_confirmed_account_id IS NULL AND receiver_confirmed_at IS NULL\r\n    AND receiver_rejected_account_id IS NULL AND receiver_rejected_at IS NULL\r\n    AND decision_account_id IS NULL AND decided_at IS NULL\r\n    AND (source_accepted_by_account_id IS NULL) = (source_accepted_at IS NULL)\r\n    AND (source_rejected_by_account_id IS NULL) = (source_rejected_at IS NULL)\r\n    AND (source_handover_confirmed_by_account_id IS NULL) = (source_handover_confirmed_at IS NULL)\r\n    AND (source_accepted_at IS NULL OR source_accepted_at >= created_at)\r\n    AND (source_rejected_at IS NULL OR source_rejected_at >= created_at)\r\n    AND (source_handover_confirmed_at IS NULL OR (source_accepted_at IS NOT NULL AND source_handover_confirmed_at >= source_accepted_at))\r\n    AND (status::text NOT IN ('APPROVED','COMPLETED') OR source_accepted_at IS NOT NULL)\r\n    AND (status::text NOT IN ('PENDING_CONFIRMATION','CANCELLED','REJECTED') OR source_accepted_at IS NULL)\r\n    AND (status::text <> 'REJECTED' OR (source_rejected_at IS NOT NULL AND source_rejection_reason IS NOT NULL AND btrim(source_rejection_reason) <> ''))\r\n    AND (status::text = 'REJECTED' OR source_rejected_at IS NULL)\r\n    AND (status::text <> 'COMPLETED' OR (source_handover_confirmed_at IS NOT NULL AND completed_at IS NOT NULL\r\n        AND target_handover_account_id IS NOT NULL AND completed_at >= source_handover_confirmed_at))\r\n    AND status::text IN ('PENDING_CONFIRMATION','APPROVED','COMPLETED','REJECTED','CANCELLED')\r\n))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_source_condition",
                table: "device_transfer_items",
                sql: "condition_at_source_handover IS NULL OR condition_at_source_handover IN ('GOOD','DAMAGED')");

            migrationBuilder.AddForeignKey(
                name: "FK_device_transfers_accounts_source_accepted_by_account_id",
                table: "device_transfers",
                column: "source_accepted_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_device_transfers_accounts_source_handover_confirmed_by_acco~",
                table: "device_transfers",
                column: "source_handover_confirmed_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_device_transfers_accounts_source_rejected_by_account_id",
                table: "device_transfers",
                column: "source_rejected_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM device_transfers WHERE initiation_type = 'EquipmentDepartment') THEN
                        RAISE EXCEPTION 'Equipment-initiated recovery orders exist. Restore a reviewed backup instead of discarding signed handover data.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_device_transfers_accounts_source_accepted_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_device_transfers_accounts_source_handover_confirmed_by_acco~",
                table: "device_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_device_transfers_accounts_source_rejected_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_creator_account_id_create_request_id",
                table: "device_transfers");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_source_accepted_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_source_handover_confirmed_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_source_rejected_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_equipment_recovery_workflow",
                table: "device_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_source_condition",
                table: "device_transfer_items");

            migrationBuilder.DropColumn(
                name: "create_request_hash",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "create_request_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "initiation_type",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_accepted_at",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_accepted_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_handover_confirmed_at",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_handover_confirmed_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_rejected_at",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_rejected_by_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "source_rejection_reason",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "condition_at_source_handover",
                table: "device_transfer_items");

            migrationBuilder.DropColumn(
                name: "source_handover_note",
                table: "device_transfer_items");

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_creator_account_id",
                table: "device_transfers",
                column: "creator_account_id");
        }
    }
}
