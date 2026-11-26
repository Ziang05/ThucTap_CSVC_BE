using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceCategoryHierarchyAndMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "maintenance_interval_months",
                table: "device_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "device_categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_categories_parent_id",
                table: "device_categories",
                column: "parent_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_categories_maintenance_interval",
                table: "device_categories",
                sql: "maintenance_interval_months > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_categories_parent",
                table: "device_categories",
                sql: "parent_id <> id");

            migrationBuilder.AddForeignKey(
                name: "FK_device_categories_device_categories_parent_id",
                table: "device_categories",
                column: "parent_id",
                principalTable: "device_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_categories_device_categories_parent_id",
                table: "device_categories");

            migrationBuilder.DropIndex(
                name: "IX_device_categories_parent_id",
                table: "device_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_categories_maintenance_interval",
                table: "device_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_categories_parent",
                table: "device_categories");

            migrationBuilder.DropColumn(
                name: "maintenance_interval_months",
                table: "device_categories");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "device_categories");
        }
    }
}
