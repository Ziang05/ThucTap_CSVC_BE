using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceMaintenanceSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "maintenance_period_id",
                table: "service_order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "performed_on",
                table: "service_order_items",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_maintenance_schedules",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    paused_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_maintenance_schedules", x => x.device_id);
                    table.CheckConstraint("ck_maintenance_schedules_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_device_maintenance_schedules_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_maintenance_schedules_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_maintenance_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closes_on = table.Column<DateOnly>(type: "date", nullable: true),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    interval_source_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Scheduled"),
                    completed_by_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_maintenance_periods", x => x.id);
                    table.UniqueConstraint("uq_maintenance_periods_id_device", x => new { x.id, x.device_id });
                    table.CheckConstraint("ck_maintenance_periods_completion", "(status = 'Completed') = (completed_by_item_id IS NOT NULL)");
                    table.CheckConstraint("ck_maintenance_periods_interval", "interval_months > 0");
                    table.CheckConstraint("ck_maintenance_periods_status", "status IN ('Scheduled', 'Due', 'Completed', 'Missed')");
                    table.CheckConstraint("ck_maintenance_periods_window", "(status = 'Scheduled' AND closes_on IS NULL) OR (status <> 'Scheduled' AND closes_on IS NOT NULL AND closes_on > due_on)");
                    table.ForeignKey(
                        name: "FK_device_maintenance_periods_device_categories_interval_sourc~",
                        column: x => x.interval_source_category_id,
                        principalTable: "device_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_maintenance_periods_device_maintenance_schedules_dev~",
                        column: x => x.device_id,
                        principalTable: "device_maintenance_schedules",
                        principalColumn: "device_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_items_maintenance_period",
                table: "service_order_items",
                column: "maintenance_period_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_maintenance_period_id_device_id",
                table: "service_order_items",
                columns: new[] { "maintenance_period_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "uq_service_items_id_maintenance_period",
                table: "service_order_items",
                columns: new[] { "id", "maintenance_period_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_items_maintenance_performed_on",
                table: "service_order_items",
                sql: "(maintenance_period_id IS NOT NULL AND execution_status IS NOT DISTINCT FROM 'Completed') = (performed_on IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_device_maintenance_periods_interval_source_category_id",
                table: "device_maintenance_periods",
                column: "interval_source_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_periods_status_close",
                table: "device_maintenance_periods",
                columns: new[] { "status", "closes_on" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_periods_status_due",
                table: "device_maintenance_periods",
                columns: new[] { "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "uq_maintenance_periods_device_due",
                table: "device_maintenance_periods",
                columns: new[] { "device_id", "due_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_maintenance_periods_one_due",
                table: "device_maintenance_periods",
                column: "device_id",
                unique: true,
                filter: "status = 'Due'");

            migrationBuilder.CreateIndex(
                name: "uq_maintenance_periods_one_scheduled",
                table: "device_maintenance_periods",
                column: "device_id",
                unique: true,
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_device_maintenance_schedules_created_by_account_id",
                table: "device_maintenance_schedules",
                column: "created_by_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_service_item_maintenance_period",
                table: "service_order_items",
                columns: new[] { "maintenance_period_id", "device_id" },
                principalTable: "device_maintenance_periods",
                principalColumns: new[] { "id", "device_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_maintenance_period_completed_item",
                table: "device_maintenance_periods",
                columns: new[] { "completed_by_item_id", "id" },
                principalTable: "service_order_items",
                principalColumns: new[] { "id", "maintenance_period_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_service_item_maintenance_period",
                table: "service_order_items");

            migrationBuilder.DropTable(
                name: "device_maintenance_periods");

            migrationBuilder.DropTable(
                name: "device_maintenance_schedules");

            migrationBuilder.DropIndex(
                name: "ix_service_items_maintenance_period",
                table: "service_order_items");

            migrationBuilder.DropIndex(
                name: "IX_service_order_items_maintenance_period_id_device_id",
                table: "service_order_items");

            migrationBuilder.DropIndex(
                name: "uq_service_items_id_maintenance_period",
                table: "service_order_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_items_maintenance_performed_on",
                table: "service_order_items");

            migrationBuilder.DropColumn(
                name: "maintenance_period_id",
                table: "service_order_items");

            migrationBuilder.DropColumn(
                name: "performed_on",
                table: "service_order_items");
        }
    }
}
