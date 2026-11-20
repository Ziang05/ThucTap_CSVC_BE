using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SourceRequestAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_started_at",
                table: "service_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "execution_started_by_account_id",
                table: "service_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_request_code",
                table: "service_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_request_id",
                table: "service_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                table: "procurement_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_by_account_id",
                table: "procurement_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assignee_account_id",
                table: "procurement_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_execution_started_by_account_id",
                table: "service_orders",
                column: "execution_started_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_source_request_id",
                table: "service_orders",
                column: "source_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_assigned_by_account_id",
                table: "procurement_requests",
                column: "assigned_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_assignee_account_id",
                table: "procurement_requests",
                column: "assignee_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_request_assignment",
                table: "procurement_requests",
                sql: "(assignee_account_id IS NULL AND assigned_by_account_id IS NULL AND assigned_at IS NULL) OR (assignee_account_id IS NOT NULL AND assigned_by_account_id IS NOT NULL AND assigned_at IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_procurement_requests_accounts_assigned_by_account_id",
                table: "procurement_requests",
                column: "assigned_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_procurement_requests_accounts_assignee_account_id",
                table: "procurement_requests",
                column: "assignee_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_service_orders_accounts_execution_started_by_account_id",
                table: "service_orders",
                column: "execution_started_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_service_orders_procurement_requests_source_request_id",
                table: "service_orders",
                column: "source_request_id",
                principalTable: "procurement_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(SourceExecutionIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SourceExecutionIntegritySql.Remove);
            migrationBuilder.DropForeignKey(
                name: "FK_procurement_requests_accounts_assigned_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_procurement_requests_accounts_assignee_account_id",
                table: "procurement_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_service_orders_accounts_execution_started_by_account_id",
                table: "service_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_service_orders_procurement_requests_source_request_id",
                table: "service_orders");

            migrationBuilder.DropIndex(
                name: "IX_service_orders_execution_started_by_account_id",
                table: "service_orders");

            migrationBuilder.DropIndex(
                name: "IX_service_orders_source_request_id",
                table: "service_orders");

            migrationBuilder.DropIndex(
                name: "IX_procurement_requests_assigned_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropIndex(
                name: "IX_procurement_requests_assignee_account_id",
                table: "procurement_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_request_assignment",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "execution_started_at",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "execution_started_by_account_id",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "source_request_code",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "source_request_id",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "assigned_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "assignee_account_id",
                table: "procurement_requests");
        }
    }
}
