using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentInventoryGroup19 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_notice_policies",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    minimum_notice_days = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_notice_policies", x => x.id);
                    table.CheckConstraint("ck_inv_policy", "id = 1 AND minimum_notice_days BETWEEN 1 AND 365 AND version >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_notice_policies_accounts_updated_by_account_id",
                        column: x => x.updated_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_slips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    create_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    department_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    creator_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    canceled_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_slips", x => x.id);
                    table.CheckConstraint("ck_inv_cancel_reason", "status <> 'CANCELED' OR (cancel_reason IS NOT NULL AND btrim(cancel_reason) <> '')");
                    table.CheckConstraint("ck_inv_slip_status", "status IN ('PENDING_APPROVAL','PREPARING','IN_PROGRESS','COMPLETED','CANCELED')");
                    table.CheckConstraint("ck_inv_slip_version", "version >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_slips_accounts_approved_by_account_id",
                        column: x => x.approved_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_slips_accounts_canceled_by_account_id",
                        column: x => x.canceled_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_slips_accounts_completed_by_account_id",
                        column: x => x.completed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_slips_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_slips_accounts_started_by_account_id",
                        column: x => x.started_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_slips_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_notice_policy_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<short>(type: "smallint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_data = table.Column<string>(type: "jsonb", nullable: false),
                    previous_days = table.Column<int>(type: "integer", nullable: false),
                    new_days = table.Column<int>(type: "integer", nullable: false),
                    changed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_notice_policy_history", x => x.id);
                    table.CheckConstraint("ck_inv_policy_change", "previous_days BETWEEN 1 AND 365 AND new_days BETWEEN 1 AND 365 AND previous_days <> new_days AND btrim(reason) <> ''");
                    table.ForeignKey(
                        name: "FK_inventory_notice_policy_history_accounts_changed_by_account~",
                        column: x => x.changed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_notice_policy_history_inventory_notice_policies_p~",
                        column: x => x.policy_id,
                        principalTable: "inventory_notice_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_data = table.Column<string>(type: "jsonb", nullable: false),
                    target_key = table.Column<string>(type: "text", nullable: false),
                    action_code = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_history", x => x.id);
                    table.CheckConstraint("ck_inv_history_action", "action_code IN ('CREATE','UPDATE_PLAN','PROPOSE_PLAN','PUBLISH','REJECT_PLAN','START','COMPLETE','CANCEL') AND (target_key = 'SLIP' OR target_key LIKE 'REVISION:%')");
                    table.ForeignKey(
                        name: "FK_inventory_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_history_inventory_slips_slip_id",
                        column: x => x.slip_id,
                        principalTable: "inventory_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_plan_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    planned_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notice_days_snapshot = table.Column<int>(type: "integer", nullable: true),
                    change_reason = table.Column<string>(type: "text", nullable: true),
                    diff_data = table.Column<string>(type: "jsonb", nullable: true),
                    proposed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_plan_revisions", x => x.id);
                    table.CheckConstraint("ck_inv_revision_notice", "status <> 'PUBLISHED' OR (approved_at IS NOT NULL AND approved_by_account_id IS NOT NULL AND published_at IS NOT NULL AND notice_days_snapshot >= 1 AND notice_days_snapshot IS NOT NULL AND planned_start_at >= published_at + notice_days_snapshot * interval '24 hours')");
                    table.CheckConstraint("ck_inv_revision_reason", "revision_no = 1 OR (change_reason IS NOT NULL AND btrim(change_reason) <> '')");
                    table.CheckConstraint("ck_inv_revision_state", "status IN ('DRAFT','PUBLISHED','REJECTED') AND revision_no >= 1 AND version >= 0");
                    table.CheckConstraint("ck_inv_revision_time", "planned_end_at IS NULL OR planned_end_at > planned_start_at");
                    table.ForeignKey(
                        name: "FK_inventory_plan_revisions_accounts_approved_by_account_id",
                        column: x => x.approved_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_revisions_accounts_proposed_by_account_id",
                        column: x => x.proposed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_revisions_accounts_rejected_by_account_id",
                        column: x => x.rejected_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_revisions_inventory_slips_slip_id",
                        column: x => x.slip_id,
                        principalTable: "inventory_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_status = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_notifications", x => x.id);
                    table.CheckConstraint("ck_inv_notification", "attempts >= 0 AND event_type IN ('PLAN_PUBLISHED','PLAN_UPDATED','PLAN_CANCELED','REMINDER') AND delivery_status IN ('PENDING','SENT','FAILED')");
                    table.ForeignKey(
                        name: "FK_inventory_notifications_accounts_recipient_account_id",
                        column: x => x.recipient_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_notifications_inventory_plan_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "inventory_plan_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_notifications_inventory_slips_slip_id",
                        column: x => x.slip_id,
                        principalTable: "inventory_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_plan_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    device_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    serial_snapshot = table.Column<string>(type: "text", nullable: true),
                    quantity_snapshot = table.Column<int>(type: "integer", nullable: false),
                    department_id_snapshot = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    area_path_snapshot = table.Column<string>(type: "text", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_plan_items", x => x.id);
                    table.CheckConstraint("ck_inv_item_quantity", "quantity_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_plan_items_areas_area_id_snapshot",
                        column: x => x.area_id_snapshot,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_items_departments_department_id_snapshot",
                        column: x => x.department_id_snapshot,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_items_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_plan_items_inventory_plan_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "inventory_plan_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_revision_assignees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    name_snapshot = table.Column<string>(type: "text", nullable: false),
                    assigned_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_revision_assignees", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_revision_assignees_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_revision_assignees_accounts_assigned_by_account_id",
                        column: x => x.assigned_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_revision_assignees_inventory_plan_revisions_revis~",
                        column: x => x.revision_id,
                        principalTable: "inventory_plan_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exists = table.Column<bool>(type: "boolean", nullable: false),
                    checked_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checker_code_snapshot = table.Column<string>(type: "text", nullable: false),
                    checker_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    initial_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_corrected_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_results", x => x.id);
                    table.CheckConstraint("ck_inv_result_editor", "(last_corrected_by_account_id IS NULL) = (last_corrected_at IS NULL)");
                    table.CheckConstraint("ck_inv_result_version", "version >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_results_accounts_checked_by_account_id",
                        column: x => x.checked_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_results_accounts_last_corrected_by_account_id",
                        column: x => x.last_corrected_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_results_inventory_plan_items_plan_item_id",
                        column: x => x.plan_item_id,
                        principalTable: "inventory_plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_code = table.Column<string>(type: "text", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_data = table.Column<string>(type: "jsonb", nullable: false),
                    before_exists = table.Column<bool>(type: "boolean", nullable: true),
                    after_exists = table.Column<bool>(type: "boolean", nullable: false),
                    version_before = table.Column<int>(type: "integer", nullable: true),
                    version_after = table.Column<int>(type: "integer", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_item_history", x => x.id);
                    table.CheckConstraint("ck_inv_item_history_shape", "(action_code = 'RESULT_RECORDED' AND before_exists IS NULL AND version_before IS NULL AND version_after = 0 AND reason IS NULL) OR (action_code = 'RESULT_CORRECTED' AND before_exists IS NOT NULL AND before_exists <> after_exists AND version_before IS NOT NULL AND version_before >= 0 AND version_after = version_before + 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
                    table.ForeignKey(
                        name: "FK_inventory_item_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_item_history_inventory_results_result_id",
                        column: x => x.result_id,
                        principalTable: "inventory_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "inventory_notice_policies",
                columns: new[] { "id", "minimum_notice_days", "updated_at", "updated_by_account_id", "version" },
                values: new object[] { (short)1, 3, new DateTimeOffset(new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 0 });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_history_actor_account_id",
                table: "inventory_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_history_request_id",
                table: "inventory_history",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_history_slip_id_occurred_at_id",
                table: "inventory_history",
                columns: new[] { "slip_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_history_actor_account_id",
                table: "inventory_item_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_history_request_id",
                table: "inventory_item_history",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_history_result_id_version_after",
                table: "inventory_item_history",
                columns: new[] { "result_id", "version_after" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notice_policies_updated_by_account_id",
                table: "inventory_notice_policies",
                column: "updated_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notice_policy_history_changed_by_account_id",
                table: "inventory_notice_policy_history",
                column: "changed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notice_policy_history_policy_id",
                table: "inventory_notice_policy_history",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notice_policy_history_request_id",
                table: "inventory_notice_policy_history",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notifications_event_key",
                table: "inventory_notifications",
                column: "event_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notifications_recipient_account_id_published_at_id",
                table: "inventory_notifications",
                columns: new[] { "recipient_account_id", "published_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notifications_revision_id",
                table: "inventory_notifications",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_notifications_slip_id",
                table: "inventory_notifications",
                column: "slip_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_items_area_id_snapshot",
                table: "inventory_plan_items",
                column: "area_id_snapshot");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_items_department_id_snapshot",
                table: "inventory_plan_items",
                column: "department_id_snapshot");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_items_device_id",
                table: "inventory_plan_items",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_items_revision_id_device_id",
                table: "inventory_plan_items",
                columns: new[] { "revision_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_revisions_approved_by_account_id",
                table: "inventory_plan_revisions",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_revisions_proposed_by_account_id",
                table: "inventory_plan_revisions",
                column: "proposed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_revisions_rejected_by_account_id",
                table: "inventory_plan_revisions",
                column: "rejected_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_revisions_slip_id_revision_no",
                table: "inventory_plan_revisions",
                columns: new[] { "slip_id", "revision_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_plan_revisions_status_planned_start_at",
                table: "inventory_plan_revisions",
                columns: new[] { "status", "planned_start_at" });

            migrationBuilder.CreateIndex(
                name: "uq_inv_one_draft_per_slip",
                table: "inventory_plan_revisions",
                column: "slip_id",
                unique: true,
                filter: "status = 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_results_checked_by_account_id_checked_at",
                table: "inventory_results",
                columns: new[] { "checked_by_account_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_results_initial_request_id",
                table: "inventory_results",
                column: "initial_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_results_last_corrected_by_account_id",
                table: "inventory_results",
                column: "last_corrected_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_results_plan_item_id",
                table: "inventory_results",
                column: "plan_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_revision_assignees_account_id",
                table: "inventory_revision_assignees",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_revision_assignees_assigned_by_account_id",
                table: "inventory_revision_assignees",
                column: "assigned_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_revision_assignees_revision_id_account_id",
                table: "inventory_revision_assignees",
                columns: new[] { "revision_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_approved_by_account_id",
                table: "inventory_slips",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_canceled_by_account_id",
                table: "inventory_slips",
                column: "canceled_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_completed_by_account_id",
                table: "inventory_slips",
                column: "completed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_create_request_id",
                table: "inventory_slips",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_created_by_account_id",
                table: "inventory_slips",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_department_id_status_created_at",
                table: "inventory_slips",
                columns: new[] { "department_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_display_code",
                table: "inventory_slips",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_slips_started_by_account_id",
                table: "inventory_slips",
                column: "started_by_account_id");
            migrationBuilder.Sql(InventoryIntegritySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InventoryIntegritySql.Down);
            migrationBuilder.DropTable(
                name: "inventory_history");

            migrationBuilder.DropTable(
                name: "inventory_item_history");

            migrationBuilder.DropTable(
                name: "inventory_notice_policy_history");

            migrationBuilder.DropTable(
                name: "inventory_notifications");

            migrationBuilder.DropTable(
                name: "inventory_revision_assignees");

            migrationBuilder.DropTable(
                name: "inventory_results");

            migrationBuilder.DropTable(
                name: "inventory_notice_policies");

            migrationBuilder.DropTable(
                name: "inventory_plan_items");

            migrationBuilder.DropTable(
                name: "inventory_plan_revisions");

            migrationBuilder.DropTable(
                name: "inventory_slips");
        }
    }
}
