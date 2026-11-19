using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementFulfillmentAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "specialized_handling_reason",
                table: "service_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acceptance_decision",
                table: "procurement_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acceptance_reason",
                table: "procurement_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acceptance_scope_hash",
                table: "procurement_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_at",
                table: "procurement_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "accepted_by_account_id",
                table: "procurement_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_evidence_json",
                table: "procurement_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "service_recorded_at",
                table: "procurement_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_recorded_by_account_id",
                table: "procurement_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workflow_version",
                table: "procurement_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "command_id",
                table: "procurement_request_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_accepted_by_account_id",
                table: "procurement_requests",
                column: "accepted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_requests_service_recorded_by_account_id",
                table: "procurement_requests",
                column: "service_recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_request_events_command_id",
                table: "procurement_request_events",
                column: "command_id",
                unique: true,
                filter: "command_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_procurement_requests_accounts_accepted_by_account_id",
                table: "procurement_requests",
                column: "accepted_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_procurement_requests_accounts_service_recorded_by_account_id",
                table: "procurement_requests",
                column: "service_recorded_by_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_procurement_requests_accounts_accepted_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_procurement_requests_accounts_service_recorded_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropIndex(
                name: "IX_procurement_requests_accepted_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropIndex(
                name: "IX_procurement_requests_service_recorded_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropIndex(
                name: "IX_procurement_request_events_command_id",
                table: "procurement_request_events");

            migrationBuilder.DropColumn(
                name: "specialized_handling_reason",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "acceptance_decision",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "acceptance_reason",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "acceptance_scope_hash",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "accepted_at",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "accepted_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "service_evidence_json",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "service_recorded_at",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "service_recorded_by_account_id",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "workflow_version",
                table: "procurement_requests");

            migrationBuilder.DropColumn(
                name: "command_id",
                table: "procurement_request_events");
        }
    }
}
