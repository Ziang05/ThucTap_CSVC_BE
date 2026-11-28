using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceDamageSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "damage_session_id",
                table: "service_order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_damage_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_damage_sessions", x => x.id);
                    table.UniqueConstraint("uq_device_damage_sessions_id_device", x => new { x.id, x.device_id });
                    table.CheckConstraint("ck_damage_session_closed", "(closed_at IS NULL) = (closed_by_account_id IS NULL) AND (closed_at IS NULL OR closed_at >= opened_at)");
                    table.CheckConstraint("ck_damage_session_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_device_damage_sessions_accounts_closed_by_account_id",
                        column: x => x.closed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_damage_sessions_accounts_opened_by_account_id",
                        column: x => x.opened_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_damage_sessions_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_report_decisions",
                columns: table => new
                {
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_report_decisions", x => x.damage_report_id);
                    table.CheckConstraint("ck_damage_report_decision_outcome", "(outcome = 'ConfirmedDamaged' AND damage_session_id IS NOT NULL OR outcome = 'ConfirmedNotDamaged' AND damage_session_id IS NULL) AND btrim(reason) <> ''");
                    table.ForeignKey(
                        name: "FK_damage_report_decisions_accounts_decided_by_account_id",
                        column: x => x.decided_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_decisions_damage_reports_damage_report_id",
                        column: x => x.damage_report_id,
                        principalTable: "damage_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_report_decisions_device_damage_sessions_damage_sessi~",
                        columns: x => new { x.damage_session_id, x.device_id },
                        principalTable: "device_damage_sessions",
                        principalColumns: new[] { "id", "device_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "damage_session_inspections",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    inspected_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_damage_session_inspections", x => x.request_id);
                    table.CheckConstraint("ck_damage_session_inspection", "expected_version > 0 AND btrim(notes) <> '' AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_damage_session_inspections_accounts_inspected_by_account_id",
                        column: x => x.inspected_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_session_inspections_device_damage_sessions_damage_se~",
                        column: x => x.damage_session_id,
                        principalTable: "device_damage_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_damage_session_inspections_service_order_items_service_orde~",
                        column: x => x.service_order_item_id,
                        principalTable: "service_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_damage_session_id",
                table: "service_order_items",
                column: "damage_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_damage_session_id_device_id",
                table: "service_order_items",
                columns: new[] { "damage_session_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_decisions_damage_session_id_device_id",
                table: "damage_report_decisions",
                columns: new[] { "damage_session_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_decisions_decided_by_account_id",
                table: "damage_report_decisions",
                column: "decided_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_report_decisions_outcome",
                table: "damage_report_decisions",
                column: "outcome");

            migrationBuilder.CreateIndex(
                name: "IX_damage_session_inspections_inspected_by_account_id",
                table: "damage_session_inspections",
                column: "inspected_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_damage_session_inspections_service_order_item_id",
                table: "damage_session_inspections",
                column: "service_order_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_damage_session_one_passing_inspection",
                table: "damage_session_inspections",
                column: "damage_session_id",
                unique: true,
                filter: "passed");

            migrationBuilder.CreateIndex(
                name: "IX_device_damage_sessions_closed_by_account_id",
                table: "device_damage_sessions",
                column: "closed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_damage_sessions_device_id_opened_at",
                table: "device_damage_sessions",
                columns: new[] { "device_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "IX_device_damage_sessions_opened_by_account_id",
                table: "device_damage_sessions",
                column: "opened_by_account_id");

            migrationBuilder.CreateIndex(
                name: "uq_device_damage_sessions_one_open",
                table: "device_damage_sessions",
                column: "device_id",
                unique: true,
                filter: "closed_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_service_order_items_device_damage_sessions_damage_session_i~",
                table: "service_order_items",
                columns: new[] { "damage_session_id", "device_id" },
                principalTable: "device_damage_sessions",
                principalColumns: new[] { "id", "device_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(DamageSessionIntegritySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DamageSessionIntegritySql.Down);

            migrationBuilder.DropForeignKey(
                name: "FK_service_order_items_device_damage_sessions_damage_session_i~",
                table: "service_order_items");

            migrationBuilder.DropTable(
                name: "damage_report_decisions");

            migrationBuilder.DropTable(
                name: "damage_session_inspections");

            migrationBuilder.DropTable(
                name: "device_damage_sessions");

            migrationBuilder.DropIndex(
                name: "IX_service_order_items_damage_session_id",
                table: "service_order_items");

            migrationBuilder.DropIndex(
                name: "IX_service_order_items_damage_session_id_device_id",
                table: "service_order_items");

            migrationBuilder.DropColumn(
                name: "damage_session_id",
                table: "service_order_items");
        }
    }
}
