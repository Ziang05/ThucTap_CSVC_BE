using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimulatedPmmsRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "procurement_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requesting_department_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contract_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    source_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_version = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mapping_issue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    pending_source_payload = table.Column<string>(type: "jsonb", nullable: true),
                    result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    result_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_requests", x => x.id);
                    table.CheckConstraint("ck_procurement_versions", "version > 0 AND source_version > 0");
                });

            migrationBuilder.CreateTable(
                name: "procurement_document_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bindings_json = table.Column<string>(type: "jsonb", nullable: false),
                    linked_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_document_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_procurement_document_links_accounts_linked_by_account_id",
                        column: x => x.linked_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procurement_document_links_procurement_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "procurement_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_request_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_version = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_request_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_procurement_request_events_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procurement_request_events_procurement_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "procurement_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_request_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    category_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    device_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    damage_report_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    specialization = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_request_lines", x => x.id);
                    table.CheckConstraint("ck_procurement_line_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "FK_procurement_request_lines_procurement_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "procurement_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_procurement_document_links_kind_document_code",
                table: "procurement_document_links",
                columns: new[] { "kind", "document_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurement_document_links_linked_by_account_id",
                table: "procurement_document_links",
                column: "linked_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_document_links_request_id",
                table: "procurement_document_links",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_request_events_actor_account_id",
                table: "procurement_request_events",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_request_events_external_event_id",
                table: "procurement_request_events",
                column: "external_event_id",
                unique: true,
                filter: "external_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_request_events_request_id_source_version",
                table: "procurement_request_events",
                columns: new[] { "request_id", "source_version" },
                unique: true,
                filter: "source_version IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_request_lines_request_id_source_line_code",
                table: "procurement_request_lines",
                columns: new[] { "request_id", "source_line_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_display_code",
                table: "procurement_requests",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_source_external_code",
                table: "procurement_requests",
                columns: new[] { "source", "external_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_status_received_at",
                table: "procurement_requests",
                columns: new[] { "status", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "procurement_document_links");

            migrationBuilder.DropTable(
                name: "procurement_request_events");

            migrationBuilder.DropTable(
                name: "procurement_request_lines");

            migrationBuilder.DropTable(
                name: "procurement_requests");
        }
    }
}
