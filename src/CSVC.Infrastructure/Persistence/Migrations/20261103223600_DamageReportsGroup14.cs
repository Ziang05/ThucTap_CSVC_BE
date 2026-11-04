using System;
using CSVC.Core.Common;
using CSVC.Core.DamageReports;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DamageReportsGroup14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
                .Annotation("Npgsql:Enum:damage_report_incident_type", "ABNORMAL,DAMAGE,MISSING")
                .Annotation("Npgsql:Enum:device_code_source", "GENERATED,MANUAL")
                .Annotation("Npgsql:Enum:device_condition", "DAMAGED,GOOD,MISSING")
                .Annotation("Npgsql:Enum:device_loan_status", "APPROVED,CANCELLED,ON_LOAN,PENDING_APPROVAL,REJECTED,RETURNED")
                .Annotation("Npgsql:Enum:device_receipt_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,REJECTED")
                .Annotation("Npgsql:Enum:device_transfer_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,PENDING_CONFIRMATION,REJECTED")
                .Annotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED")
                .OldAnnotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
                .OldAnnotation("Npgsql:Enum:device_code_source", "GENERATED,MANUAL")
                .OldAnnotation("Npgsql:Enum:device_condition", "DAMAGED,GOOD,MISSING")
                .OldAnnotation("Npgsql:Enum:device_loan_status", "APPROVED,CANCELLED,ON_LOAN,PENDING_APPROVAL,REJECTED,RETURNED")
                .OldAnnotation("Npgsql:Enum:device_receipt_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,REJECTED")
                .OldAnnotation("Npgsql:Enum:device_transfer_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,PENDING_CONFIRMATION,REJECTED")
                .OldAnnotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED");

            migrationBuilder.CreateTable(
                name: "damage_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporting_department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_type = table.Column<DamageReportIncidentType>(type: "damage_report_incident_type", nullable: false),
                    incident_description = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    create_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_reports", x => x.id);
                    table.CheckConstraint("ck_damage_report_content", "btrim(report_code) <> '' AND btrim(incident_description) <> '' AND length(incident_description) <= 10000 AND (notes IS NULL OR length(notes) <= 4000)");
                    table.CheckConstraint("ck_damage_report_editor", "(last_updated_at IS NULL) = (last_updated_by_account_id IS NULL)");
                    table.CheckConstraint("ck_damage_report_hash", "create_request_hash ~ '^[0-9A-F]{64}$' AND create_request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_damage_report_time_version", "detected_at <= reported_at AND row_version > 0 AND (last_updated_at IS NULL OR last_updated_at >= reported_at)");
                    table.ForeignKey(
                        name: "FK_damage_reports_accounts_last_updated_by_account_id",
                        column: x => x.last_updated_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_reports_accounts_reporter_account_id",
                        column: x => x.reporter_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_reports_departments_reporting_department_id",
                        column: x => x.reporting_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_reports_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_contexts",
                columns: table => new
                {
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reporting_department_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_code_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    device_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_serial_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device_department_id_at_report = table.Column<Guid>(type: "uuid", nullable: false),
                    device_department_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_area_id_at_report = table.Column<Guid>(type: "uuid", nullable: true),
                    device_area_path_snapshot = table.Column<string>(type: "text", nullable: true),
                    device_context_captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_contexts", x => x.damage_report_id);
                    table.ForeignKey(
                        name: "FK_damage_report_contexts_areas_device_area_id_at_report",
                        column: x => x.device_area_id_at_report,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_contexts_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_contexts_departments_device_department_id_at_~",
                        column: x => x.device_department_id_at_report,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    storage_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    original_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_evidence", x => x.id);
                    table.CheckConstraint("ck_damage_evidence_removal", "(removed_at IS NULL) = (removed_by_account_id IS NULL) AND (removed_at IS NULL OR removed_at >= uploaded_at)");
                    table.CheckConstraint("ck_damage_evidence_size_sequence", "size_bytes > 0 AND size_bytes <= 5242880 AND sequence_no > 0");
                    table.CheckConstraint("ck_damage_evidence_type_hash", "content_type IN ('image/png','image/jpeg') AND sha256_hex ~ '^[0-9A-F]{64}$'");
                    table.ForeignKey(
                        name: "FK_damage_report_evidence_accounts_removed_by_account_id",
                        column: x => x.removed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_evidence_accounts_uploaded_by_account_id",
                        column: x => x.uploaded_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_evidence_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_damage_report_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_history_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_pending_devices",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_pending_devices", x => x.device_id);
                    table.ForeignKey(
                        name: "FK_damage_report_pending_devices_damage_reports_damage_report_~",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_pending_devices_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_receptions",
                columns: table => new
                {
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_receptions", x => x.damage_report_id);
                    table.ForeignKey(
                        name: "FK_damage_report_receptions_accounts_received_by_account_id",
                        column: x => x.received_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_receptions_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_requests",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_requests", x => x.request_id);
                    table.ForeignKey(
                        name: "FK_damage_report_requests_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_requests_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_contexts_device_area_id_at_report",
                table: "damage_report_contexts",
                column: "device_area_id_at_report");

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_contexts_device_department_id_at_report_damag~",
                table: "damage_report_contexts",
                columns: new[] { "device_department_id_at_report", "damage_report_id" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_evidence_damage_report_id_removed_at",
                table: "damage_report_evidence",
                columns: new[] { "damage_report_id", "removed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_evidence_damage_report_id_sequence_no",
                table: "damage_report_evidence",
                columns: new[] { "damage_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_evidence_removed_by_account_id",
                table: "damage_report_evidence",
                column: "removed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_evidence_storage_object_key",
                table: "damage_report_evidence",
                column: "storage_object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_evidence_uploaded_by_account_id",
                table: "damage_report_evidence",
                column: "uploaded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_history_actor_account_id_occurred_at",
                table: "damage_report_history",
                columns: new[] { "actor_account_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_history_damage_report_id_occurred_at",
                table: "damage_report_history",
                columns: new[] { "damage_report_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_history_request_id_action_code_damage_report_~",
                table: "damage_report_history",
                columns: new[] { "request_id", "action_code", "damage_report_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_pending_devices_damage_report_id",
                table: "damage_report_pending_devices",
                column: "damage_report_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_receptions_received_by_account_id_received_at",
                table: "damage_report_receptions",
                columns: new[] { "received_by_account_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_receptions_request_id",
                table: "damage_report_receptions",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_requests_actor_account_id",
                table: "damage_report_requests",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_requests_damage_report_id",
                table: "damage_report_requests",
                column: "damage_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_create_request_id",
                table: "damage_reports",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_device_id_reported_at_id",
                table: "damage_reports",
                columns: new[] { "device_id", "reported_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_incident_type_reported_at",
                table: "damage_reports",
                columns: new[] { "incident_type", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_last_updated_by_account_id",
                table: "damage_reports",
                column: "last_updated_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_report_code",
                table: "damage_reports",
                column: "report_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_reported_at_id",
                table: "damage_reports",
                columns: new[] { "reported_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_reporter_account_id_reported_at",
                table: "damage_reports",
                columns: new[] { "reporter_account_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_reporting_department_id_reported_at_id",
                table: "damage_reports",
                columns: new[] { "reporting_department_id", "reported_at", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_damage_reports_id_device_for_pending_fk",
                table: "damage_reports",
                columns: new[] { "id", "device_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_repair_item_details_damage_reports_damage_report_id",
                table: "repair_item_details",
                column: "damage_report_id",
                principalTable: "damage_reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(DamageReportIntegritySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DamageReportIntegritySql.Down);
            migrationBuilder.DropForeignKey(
                name: "FK_repair_item_details_damage_reports_damage_report_id",
                table: "repair_item_details");

            migrationBuilder.DropTable(
                name: "damage_report_contexts");

            migrationBuilder.DropTable(
                name: "damage_report_evidence");

            migrationBuilder.DropTable(
                name: "damage_report_history");

            migrationBuilder.DropTable(
                name: "damage_report_pending_devices");

            migrationBuilder.DropTable(
                name: "damage_report_receptions");

            migrationBuilder.DropTable(
                name: "damage_report_requests");

            migrationBuilder.DropTable(
                name: "damage_reports");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
                .Annotation("Npgsql:Enum:device_code_source", "GENERATED,MANUAL")
                .Annotation("Npgsql:Enum:device_condition", "DAMAGED,GOOD,MISSING")
                .Annotation("Npgsql:Enum:device_loan_status", "APPROVED,CANCELLED,ON_LOAN,PENDING_APPROVAL,REJECTED,RETURNED")
                .Annotation("Npgsql:Enum:device_receipt_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,REJECTED")
                .Annotation("Npgsql:Enum:device_transfer_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,PENDING_CONFIRMATION,REJECTED")
                .Annotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED")
                .OldAnnotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
                .OldAnnotation("Npgsql:Enum:damage_report_incident_type", "ABNORMAL,DAMAGE,MISSING")
                .OldAnnotation("Npgsql:Enum:device_code_source", "GENERATED,MANUAL")
                .OldAnnotation("Npgsql:Enum:device_condition", "DAMAGED,GOOD,MISSING")
                .OldAnnotation("Npgsql:Enum:device_loan_status", "APPROVED,CANCELLED,ON_LOAN,PENDING_APPROVAL,REJECTED,RETURNED")
                .OldAnnotation("Npgsql:Enum:device_receipt_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,REJECTED")
                .OldAnnotation("Npgsql:Enum:device_transfer_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,PENDING_CONFIRMATION,REJECTED")
                .OldAnnotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED");
        }
    }
}
