using System;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceExportReturnV9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ServiceExportReturnIntegritySql.Prepare);
            migrationBuilder.DropForeignKey(
                name: "FK_export_slips_accounts_result_recorded_by_account_id",
                table: "export_slips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_items_result",
                table: "service_order_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_result_recorded_by_account_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_result_request_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_result_result_recorded_at",
                table: "export_slips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_slips_result",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slip_items_service_order_item_id",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_history_request_id_action_code_export_slip_id",
                table: "export_history");

            migrationBuilder.DropColumn(
                name: "evidence_ref",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "recipient_contact",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "recipient_name",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "result",
                table: "export_slips");

            migrationBuilder.DropColumn(name: "result_request_id", table: "export_slips");
            migrationBuilder.AddColumn<Guid>(name: "last_edited_by_account_id", table: "export_slips", type: "uuid", nullable: true);

            migrationBuilder.DropColumn(name: "result_recorded_by_account_id", table: "export_slips");
            migrationBuilder.AddColumn<Guid>(name: "edit_request_id", table: "export_slips", type: "uuid", nullable: true);

            migrationBuilder.DropColumn(name: "result_recorded_at", table: "export_slips");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "last_edited_at", table: "export_slips", type: "timestamp with time zone", nullable: true);

            migrationBuilder.DropColumn(name: "failure_reason", table: "export_slips");
            migrationBuilder.AddColumn<string>(name: "cancel_reason", table: "export_slips", type: "text", nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                table: "export_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "export_slips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_account_id",
                table: "export_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancel_request_id",
                table: "export_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "canceled_by_account_id",
                table: "export_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "create_request_hash",
                table: "export_slips",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "create_request_id",
                table: "export_slips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "evidence_ref",
                table: "export_slip_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "export_slip_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_contact",
                table: "export_slip_items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_name",
                table: "export_slip_items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result",
                table: "export_slip_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "result_recorded_at",
                table: "export_slip_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "result_recorded_by_account_id",
                table: "export_slip_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "result_request_id",
                table: "export_slip_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "export_slip_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "export_slip_item_id",
                table: "export_history",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_payload_hash",
                table: "export_history",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_key",
                table: "export_history",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "uq_service_order_item_hold_fk",
                table: "service_order_items",
                columns: new[] { "id", "service_order_id", "device_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "uq_export_slip_items_id_export",
                table: "export_slip_items",
                columns: new[] { "id", "export_slip_id" });

            migrationBuilder.CreateTable(
                name: "export_device_guards",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guard_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_device_guards", x => x.device_id);
                    table.CheckConstraint("ck_export_guard_state", "guard_state IN ('HeldForExport','AwaitingReturn') AND version > 0 AND changed_at >= acquired_at");
                    table.ForeignKey(
                        name: "FK_export_device_guards_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_device_guards_export_slip_items_export_slip_item_id",
                        column: x => x.export_slip_item_id,
                        principalTable: "export_slip_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_return_slips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    display_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "Completed"),
                    facility_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    handover_person_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    handover_person_contact = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    received_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    create_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_return_slips", x => x.id);
                    table.UniqueConstraint("uq_export_return_slips_source_id", x => new { x.id, x.export_slip_id });
                    table.CheckConstraint("ck_export_return_header", "status = 'Completed' AND sequence_no > 0 AND btrim(display_code) <> '' AND btrim(facility_name_snapshot) <> '' AND btrim(handover_person_name) <> '' AND received_at <= created_at AND version > 0");
                    table.ForeignKey(
                        name: "FK_export_return_slips_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_return_slips_accounts_received_by_account_id",
                        column: x => x.received_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_return_slips_export_slips_export_slip_id",
                        column: x => x.export_slip_id,
                        principalTable: "export_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_order_device_holds",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    held_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_order_device_holds", x => x.device_id);
                    table.CheckConstraint("ck_service_hold_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_service_order_device_holds_accounts_held_by_account_id",
                        column: x => x.held_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_device_holds_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_device_holds_service_order_items_service_orde~",
                        columns: x => new { x.service_order_item_id, x.service_order_id, x.device_id },
                        principalTable: "service_order_items",
                        principalColumns: new[] { "id", "service_order_id", "device_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_device_holds_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_return_slip_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_snapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    serial_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    condition_at_return = table.Column<DeviceCondition>(type: "device_condition", nullable: false),
                    return_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_return_slip_items", x => x.id);
                    table.CheckConstraint("ck_export_return_condition", "condition_at_return IN ('GOOD','DAMAGED')");
                    table.ForeignKey(
                        name: "FK_export_return_slip_items_export_return_slips_return_slip_id~",
                        columns: x => new { x.return_slip_id, x.export_slip_id },
                        principalTable: "export_return_slips",
                        principalColumns: new[] { "id", "export_slip_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_return_slip_items_export_slip_items_export_slip_item~",
                        columns: x => new { x.export_slip_item_id, x.export_slip_id },
                        principalTable: "export_slip_items",
                        principalColumns: new[] { "id", "export_slip_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_return_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_slip_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_return_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_export_return_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_return_history_export_return_slip_items_return_slip_~",
                        column: x => x.return_slip_item_id,
                        principalTable: "export_return_slip_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_return_history_export_return_slips_return_slip_id",
                        column: x => x.return_slip_id,
                        principalTable: "export_return_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_items_result",
                table: "service_order_items",
                sql: "execution_status NOT IN ('NotExecuted','Completed','Failed') OR (result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND COALESCE(btrim(result_description), '') <> '')");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_approval_request_id",
                table: "export_slips",
                column: "approval_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_approved_by_account_id",
                table: "export_slips",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_cancel_request_id",
                table: "export_slips",
                column: "cancel_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_canceled_by_account_id",
                table: "export_slips",
                column: "canceled_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_create_request_id",
                table: "export_slips",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_last_edited_by_account_id",
                table: "export_slips",
                column: "last_edited_by_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_slips_approval",
                table: "export_slips",
                sql: "status NOT IN ('InProgress','Completed') OR (approved_by_account_id IS NOT NULL AND approved_at IS NOT NULL AND approval_request_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_slips_cancel",
                table: "export_slips",
                sql: "status <> 'Canceled' OR (canceled_by_account_id IS NOT NULL AND canceled_at IS NOT NULL AND cancel_request_id IS NOT NULL AND COALESCE(btrim(cancel_reason),'') <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_slips_completed",
                table: "export_slips",
                sql: "(status = 'Completed') = (completed_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_result_recorded_by_account_id",
                table: "export_slip_items",
                column: "result_recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_result_request_id",
                table: "export_slip_items",
                column: "result_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_result_result_recorded_at",
                table: "export_slip_items",
                columns: new[] { "result", "result_recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_service_order_item_id",
                table: "export_slip_items",
                column: "service_order_item_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_item_result",
                table: "export_slip_items",
                sql: "(result IS NULL AND result_recorded_by_account_id IS NULL AND result_recorded_at IS NULL AND result_request_id IS NULL AND recipient_name IS NULL AND recipient_contact IS NULL AND failure_reason IS NULL AND evidence_ref IS NULL) OR (result IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND result_recorded_at IS NOT NULL AND result_request_id IS NOT NULL AND ((result = 'Success' AND COALESCE(btrim(recipient_name),'') <> '' AND failure_reason IS NULL) OR (result = 'Failed' AND COALESCE(btrim(failure_reason),'') <> '' AND recipient_name IS NULL AND recipient_contact IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_export_history_export_slip_item_id",
                table: "export_history",
                column: "export_slip_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_history_request_id_action_code_export_slip_id_target~",
                table: "export_history",
                columns: new[] { "request_id", "action_code", "export_slip_id", "target_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_device_guards_export_slip_item_id",
                table: "export_device_guards",
                column: "export_slip_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_device_guards_guard_state_changed_at",
                table: "export_device_guards",
                columns: new[] { "guard_state", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_history_actor_account_id_occurred_at",
                table: "export_return_history",
                columns: new[] { "actor_account_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_history_request_id_action_code_return_slip_id~",
                table: "export_return_history",
                columns: new[] { "request_id", "action_code", "return_slip_id", "target_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_history_return_slip_id_occurred_at",
                table: "export_return_history",
                columns: new[] { "return_slip_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_history_return_slip_item_id_occurred_at",
                table: "export_return_history",
                columns: new[] { "return_slip_item_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_condition_at_return",
                table: "export_return_slip_items",
                column: "condition_at_return");

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_export_slip_id_return_slip_id",
                table: "export_return_slip_items",
                columns: new[] { "export_slip_id", "return_slip_id" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_export_slip_item_id",
                table: "export_return_slip_items",
                column: "export_slip_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_export_slip_item_id_export_slip_id",
                table: "export_return_slip_items",
                columns: new[] { "export_slip_item_id", "export_slip_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_return_slip_id_export_slip_id",
                table: "export_return_slip_items",
                columns: new[] { "return_slip_id", "export_slip_id" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slip_items_return_slip_id_export_slip_item_id",
                table: "export_return_slip_items",
                columns: new[] { "return_slip_id", "export_slip_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_create_request_id",
                table: "export_return_slips",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_created_by_account_id_created_at",
                table: "export_return_slips",
                columns: new[] { "created_by_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_display_code",
                table: "export_return_slips",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_export_slip_id_received_at",
                table: "export_return_slips",
                columns: new[] { "export_slip_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_export_slip_id_sequence_no",
                table: "export_return_slips",
                columns: new[] { "export_slip_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_return_slips_received_by_account_id_received_at",
                table: "export_return_slips",
                columns: new[] { "received_by_account_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_device_holds_held_by_account_id",
                table: "service_order_device_holds",
                column: "held_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_device_holds_service_order_id_held_at",
                table: "service_order_device_holds",
                columns: new[] { "service_order_id", "held_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_device_holds_service_order_item_id",
                table: "service_order_device_holds",
                column: "service_order_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_order_device_holds_service_order_item_id_service_or~",
                table: "service_order_device_holds",
                columns: new[] { "service_order_item_id", "service_order_id", "device_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_export_history_export_slip_items_export_slip_item_id",
                table: "export_history",
                column: "export_slip_item_id",
                principalTable: "export_slip_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_export_slip_items_accounts_result_recorded_by_account_id",
                table: "export_slip_items",
                column: "result_recorded_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_export_slips_accounts_approved_by_account_id",
                table: "export_slips",
                column: "approved_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_export_slips_accounts_canceled_by_account_id",
                table: "export_slips",
                column: "canceled_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_export_slips_accounts_last_edited_by_account_id",
                table: "export_slips",
                column: "last_edited_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(ServiceExportReturnIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ServiceExportReturnIntegritySql.Remove);
            migrationBuilder.DropForeignKey(
                name: "FK_export_history_export_slip_items_export_slip_item_id",
                table: "export_history");

            migrationBuilder.DropForeignKey(
                name: "FK_export_slip_items_accounts_result_recorded_by_account_id",
                table: "export_slip_items");

            migrationBuilder.DropForeignKey(
                name: "FK_export_slips_accounts_approved_by_account_id",
                table: "export_slips");

            migrationBuilder.DropForeignKey(
                name: "FK_export_slips_accounts_canceled_by_account_id",
                table: "export_slips");

            migrationBuilder.DropForeignKey(
                name: "FK_export_slips_accounts_last_edited_by_account_id",
                table: "export_slips");

            migrationBuilder.DropTable(
                name: "export_device_guards");

            migrationBuilder.DropTable(
                name: "export_return_history");

            migrationBuilder.DropTable(
                name: "service_order_device_holds");

            migrationBuilder.DropTable(
                name: "export_return_slip_items");

            migrationBuilder.DropTable(
                name: "export_return_slips");

            migrationBuilder.DropUniqueConstraint(
                name: "uq_service_order_item_hold_fk",
                table: "service_order_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_items_result",
                table: "service_order_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_approval_request_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_approved_by_account_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_cancel_request_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_canceled_by_account_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_create_request_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_last_edited_by_account_id",
                table: "export_slips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_slips_approval",
                table: "export_slips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_slips_cancel",
                table: "export_slips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_slips_completed",
                table: "export_slips");

            migrationBuilder.DropUniqueConstraint(
                name: "uq_export_slip_items_id_export",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slip_items_result_recorded_by_account_id",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slip_items_result_request_id",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slip_items_result_result_recorded_at",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_slip_items_service_order_item_id",
                table: "export_slip_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_item_result",
                table: "export_slip_items");

            migrationBuilder.DropIndex(
                name: "IX_export_history_export_slip_item_id",
                table: "export_history");

            migrationBuilder.DropIndex(
                name: "IX_export_history_request_id_action_code_export_slip_id_target~",
                table: "export_history");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "approved_by_account_id",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "cancel_request_id",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "canceled_by_account_id",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "create_request_hash",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "create_request_id",
                table: "export_slips");

            migrationBuilder.DropColumn(
                name: "evidence_ref",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "recipient_contact",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "recipient_name",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "result",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "result_recorded_at",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "result_recorded_by_account_id",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "result_request_id",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "version",
                table: "export_slip_items");

            migrationBuilder.DropColumn(
                name: "export_slip_item_id",
                table: "export_history");

            migrationBuilder.DropColumn(
                name: "request_payload_hash",
                table: "export_history");

            migrationBuilder.DropColumn(
                name: "target_key",
                table: "export_history");

            migrationBuilder.DropColumn(name: "last_edited_by_account_id", table: "export_slips");
            migrationBuilder.AddColumn<Guid>(name: "result_request_id", table: "export_slips", type: "uuid", nullable: true);

            migrationBuilder.DropColumn(name: "last_edited_at", table: "export_slips");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "result_recorded_at", table: "export_slips", type: "timestamp with time zone", nullable: true);

            migrationBuilder.DropColumn(name: "edit_request_id", table: "export_slips");
            migrationBuilder.AddColumn<Guid>(name: "result_recorded_by_account_id", table: "export_slips", type: "uuid", nullable: true);

            migrationBuilder.DropColumn(name: "cancel_reason", table: "export_slips");
            migrationBuilder.AddColumn<string>(name: "failure_reason", table: "export_slips", type: "text", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_ref",
                table: "export_slips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_contact",
                table: "export_slips",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_name",
                table: "export_slips",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result",
                table: "export_slips",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_items_result",
                table: "service_order_items",
                sql: "execution_status NOT IN ('Completed','Failed') OR (result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND COALESCE(btrim(result_description), '') <> '')");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_recorded_by_account_id",
                table: "export_slips",
                column: "result_recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_request_id",
                table: "export_slips",
                column: "result_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_result_recorded_at",
                table: "export_slips",
                columns: new[] { "result", "result_recorded_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_slips_result",
                table: "export_slips",
                sql: "(result IS NULL AND status <> 'Completed') OR (result IS NOT NULL AND status = 'Completed' AND completed_at IS NOT NULL AND result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND ((result = 'Success' AND COALESCE(btrim(recipient_name), '') <> '') OR (result = 'Failed' AND COALESCE(btrim(failure_reason), '') <> '')))");

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_service_order_item_id",
                table: "export_slip_items",
                column: "service_order_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_history_request_id_action_code_export_slip_id",
                table: "export_history",
                columns: new[] { "request_id", "action_code", "export_slip_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_export_slips_accounts_result_recorded_by_account_id",
                table: "export_slips",
                column: "result_recorded_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION check_service_export_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE affected_item uuid; affected_slip uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'export_slip_items' THEN
                        affected_item := NEW.service_order_item_id; affected_slip := NEW.export_slip_id;
                    ELSIF TG_TABLE_NAME = 'service_order_items' THEN
                        affected_item := NEW.id;
                    ELSE
                        affected_slip := NEW.id;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM export_slip_items line
                        JOIN export_slips slip ON slip.id = line.export_slip_id
                        JOIN service_order_items item ON item.id = line.service_order_item_id
                        WHERE ((affected_item IS NOT NULL AND item.id = affected_item)
                            OR (affected_slip IS NOT NULL AND slip.id = affected_slip))
                          AND (item.service_order_id <> slip.service_order_id OR item.execution_method <> 'External')
                    ) THEN
                        RAISE EXCEPTION 'Export items must be external and belong to the source order'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_export_slip_source_membership';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER ck_export_line_membership AFTER INSERT OR UPDATE ON export_slip_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();
                CREATE CONSTRAINT TRIGGER ck_service_item_export_membership AFTER UPDATE ON service_order_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();
                CREATE CONSTRAINT TRIGGER ck_export_header_membership AFTER UPDATE ON export_slips
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();
                """);
        }
    }
}
