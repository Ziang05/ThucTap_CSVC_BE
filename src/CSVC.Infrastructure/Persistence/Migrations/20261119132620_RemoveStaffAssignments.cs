using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStaffAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve earlier assignment records and their links outside the active application model.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS retired_assignment_context (
                    kind text NOT NULL, id uuid NOT NULL, payload jsonb NOT NULL,
                    archived_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (kind, id));
                INSERT INTO retired_assignment_context(kind,id,payload)
                  SELECT 'task',id,to_jsonb(t) FROM work_tasks t
                  UNION ALL SELECT 'history',id,to_jsonb(h) FROM work_task_history h
                  UNION ALL SELECT 'document',id,jsonb_build_object('work_id',parent_work_task_id) FROM material_documents
                  UNION ALL SELECT 'change',id,jsonb_build_object('work_id',work_task_id) FROM equipment_changes
                  UNION ALL SELECT 'use',id,jsonb_build_object('work_id',work_task_id) FROM material_work_uses
                ON CONFLICT(kind,id) DO UPDATE SET payload=excluded.payload, archived_at=now();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_equipment_changes_work_tasks_work_task_id",
                table: "equipment_changes");

            migrationBuilder.DropForeignKey(
                name: "FK_material_documents_work_tasks_parent_work_task_id",
                table: "material_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_material_work_uses_work_tasks_work_task_id",
                table: "material_work_uses");

            migrationBuilder.DropTable(
                name: "work_task_history");

            migrationBuilder.DropTable(
                name: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_material_work_uses_work_task_id",
                table: "material_work_uses");

            migrationBuilder.DropIndex(
                name: "IX_material_documents_parent_work_task_id",
                table: "material_documents");

            migrationBuilder.DropIndex(
                name: "IX_equipment_changes_work_task_id",
                table: "equipment_changes");

            migrationBuilder.DropColumn(
                name: "work_task_id",
                table: "material_work_uses");

            migrationBuilder.DropColumn(
                name: "parent_work_task_id",
                table: "material_documents");

            migrationBuilder.DropColumn(
                name: "work_task_id",
                table: "equipment_changes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "work_task_id",
                table: "material_work_uses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "parent_work_task_id",
                table: "material_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "work_task_id",
                table: "equipment_changes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "work_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: false),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "IX_material_work_uses_work_task_id",
                table: "material_work_uses",
                column: "work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_parent_work_task_id",
                table: "material_documents",
                column: "parent_work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_work_task_id",
                table: "equipment_changes",
                column: "work_task_id");

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

            migrationBuilder.Sql("""
                ALTER TABLE equipment_changes DISABLE TRIGGER USER;
                ALTER TABLE material_documents DISABLE TRIGGER USER;
                ALTER TABLE material_work_uses DISABLE TRIGGER USER;
                INSERT INTO work_tasks SELECT (jsonb_populate_record(NULL::work_tasks,payload)).*
                  FROM retired_assignment_context WHERE kind='task';
                INSERT INTO work_task_history SELECT (jsonb_populate_record(NULL::work_task_history,payload)).*
                  FROM retired_assignment_context WHERE kind='history';
                UPDATE material_documents d SET parent_work_task_id=(a.payload->>'work_id')::uuid
                  FROM retired_assignment_context a WHERE a.kind='document' AND a.id=d.id;
                UPDATE equipment_changes c SET work_task_id=(a.payload->>'work_id')::uuid
                  FROM retired_assignment_context a WHERE a.kind='change' AND a.id=c.id;
                UPDATE material_work_uses u SET work_task_id=(a.payload->>'work_id')::uuid
                  FROM retired_assignment_context a WHERE a.kind='use' AND a.id=u.id;
                -- Evidence created after removal gets an explicit historical context on downgrade.
                DO $$ DECLARE row_record record; context_id uuid; BEGIN
                  FOR row_record IN
                    SELECT 'change' AS kind,c.id,c.recorded_by_account_id AS actor,d.department_id,c.recorded_at AS at
                      FROM equipment_changes c JOIN devices d ON d.id=c.device_id
                      WHERE c.work_task_id='00000000-0000-0000-0000-000000000000'
                    UNION ALL
                    SELECT 'use',u.id,u.actor_account_id,d.department_id,u.occurred_at
                      FROM material_work_uses u JOIN material_document_lines l ON l.id=u.issue_line_id
                      JOIN material_documents d ON d.id=l.document_id
                      WHERE u.work_task_id='00000000-0000-0000-0000-000000000000'
                  LOOP
                    context_id := gen_random_uuid();
                    INSERT INTO work_tasks(id,display_code,title,kind,source,source_code,department_id,
                      assigned_account_id,status,created_by_account_id,created_at,automatic,version,completed_at)
                    VALUES(context_id,'CV'||left(replace(context_id::text,'-',''),30),'Historical material evidence',
                      'Other','Manual','RESTORED',row_record.department_id,row_record.actor,'Completed',
                      row_record.actor,row_record.at,false,1,row_record.at);
                    IF row_record.kind='change' THEN UPDATE equipment_changes SET work_task_id=context_id WHERE id=row_record.id;
                    ELSE UPDATE material_work_uses SET work_task_id=context_id WHERE id=row_record.id; END IF;
                  END LOOP;
                END $$;
                ALTER TABLE equipment_changes ENABLE TRIGGER USER;
                ALTER TABLE material_documents ENABLE TRIGGER USER;
                ALTER TABLE material_work_uses ENABLE TRIGGER USER;
                """);
            migrationBuilder.AddForeignKey(
                name: "FK_equipment_changes_work_tasks_work_task_id",
                table: "equipment_changes",
                column: "work_task_id",
                principalTable: "work_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_material_documents_work_tasks_parent_work_task_id",
                table: "material_documents",
                column: "parent_work_task_id",
                principalTable: "work_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_material_work_uses_work_tasks_work_task_id",
                table: "material_work_uses",
                column: "work_task_id",
                principalTable: "work_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
