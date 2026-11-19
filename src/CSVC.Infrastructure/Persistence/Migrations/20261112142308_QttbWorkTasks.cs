using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QttbWorkTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_tasks", x => x.id);
                    table.CheckConstraint("ck_work_assignment", "(status = 'Unassigned' AND assigned_account_id IS NULL) OR (status = 'Cancelled') OR (status <> 'Unassigned' AND assigned_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_work_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_work_tasks_accounts_assigned_account_id",
                        column: x => x.assigned_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_tasks_accounts_assigned_by_account_id",
                        column: x => x.assigned_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_tasks_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_tasks_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_task_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_task_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_task_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_task_history_work_tasks_work_task_id",
                        column: x => x.work_task_id,
                        principalTable: "work_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_task_history_actor_account_id",
                table: "work_task_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_task_history_work_task_id_occurred_at",
                table: "work_task_history",
                columns: new[] { "work_task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_assigned_account_id_created_at",
                table: "work_tasks",
                columns: new[] { "assigned_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_assigned_by_account_id",
                table: "work_tasks",
                column: "assigned_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_created_by_account_id",
                table: "work_tasks",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_department_id_status_due_at",
                table: "work_tasks",
                columns: new[] { "department_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_display_code",
                table: "work_tasks",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_source_source_code_kind",
                table: "work_tasks",
                columns: new[] { "source", "source_code", "kind" },
                unique: true,
                filter: "status NOT IN ('Completed', 'Cancelled')");

            // Import open documents without inventing historical assignments or a human migration actor.
            migrationBuilder.Sql("""
                WITH sources AS (
                    SELECT 'Receipt' AS source, receipt_code AS code, 'Receipt' AS kind FROM device_receipts
                    WHERE lower(replace(status::text, '_', '')) IN ('pendingapproval','approved')
                    UNION ALL SELECT 'Transfer', transfer_code, CASE WHEN lower(transfer_type::text) = 'recovery' THEN 'Recovery' ELSE 'Handover' END FROM device_transfers
                    WHERE lower(replace(status::text, '_', '')) IN ('pendingconfirmation','pendingapproval','approved')
                    UNION ALL SELECT 'ServiceOrder', display_code, order_type FROM service_orders WHERE status IN ('PendingApproval','InProgress') OR EXISTS (SELECT 1 FROM service_order_device_holds h WHERE h.service_order_id = service_orders.id)
                    UNION ALL SELECT 'Loan', loan_code, 'BorrowReturn' FROM device_loans WHERE lower(replace(status::text, '_', '')) NOT IN ('returned','cancelled','rejected') AND EXISTS (SELECT 1 FROM equipment_settings e WHERE e.equipment_department_id = device_loans.borrower_department_id OR e.equipment_department_id = device_loans.lender_department_id)
                    UNION ALL SELECT 'ProcurementRequest', display_code, 'Intake' FROM procurement_requests WHERE status NOT IN ('Completed','Cancelled')
                )
                INSERT INTO work_tasks (id, display_code, title, kind, source, source_code, department_id, status, notes, created_at, automatic, version)
                SELECT gen_random_uuid(), 'CV' || upper(substr(replace(gen_random_uuid()::text,'-',''),1,30)), 'Công việc từ phiếu · ' || sources.code,
                    sources.kind, sources.source, sources.code, settings.equipment_department_id, 'Unassigned',
                    'Imported open document. No previous assignment has been inferred.', now(), true, 1
                FROM sources CROSS JOIN equipment_settings settings WHERE settings.equipment_department_id IS NOT NULL;
                INSERT INTO work_task_history (id, work_task_id, action, reason, after_data, occurred_at)
                SELECT gen_random_uuid(), id, 'WORK_IMPORTED', 'Open source imported without historical assignment',
                    jsonb_build_object('status','Unassigned','source',source,'sourceCode',source_code), created_at FROM work_tasks;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_task_history");

            migrationBuilder.DropTable(
                name: "work_tasks");
        }
    }
}
