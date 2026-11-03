using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentDepartmentAndTransferConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
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
                .OldAnnotation("Npgsql:Enum:device_transfer_status", "APPROVED,CANCELLED,COMPLETED,PENDING_APPROVAL,REJECTED")
                .OldAnnotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED");

            migrationBuilder.AddColumn<Guid>(
                name: "receiver_confirmed_account_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "receiver_confirmed_at",
                table: "device_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receiver_rejected_account_id",
                table: "device_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "receiver_rejected_at",
                table: "device_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_rejection_reason",
                table: "device_transfers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_type",
                table: "device_transfers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false);

            migrationBuilder.CreateTable(
                name: "equipment_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    equipment_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_settings", x => x.id);
                    table.CheckConstraint("ck_equipment_settings_singleton", "id = 1");
                    table.ForeignKey(
                        name: "FK_equipment_settings_accounts_updated_by_account_id",
                        column: x => x.updated_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_settings_departments_equipment_department_id",
                        column: x => x.equipment_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment_settings_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_settings_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_equipment_settings_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_settings_history_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_settings_history_departments_previous_department_~",
                        column: x => x.previous_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "equipment_settings",
                columns: new[] { "id", "equipment_department_id", "updated_at", "updated_by_account_id", "version" },
                values: new object[] { 1, null, null, null, 0 });

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_receiver_confirmed_account_id",
                table: "device_transfers",
                column: "receiver_confirmed_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_transfers_receiver_rejected_account_id",
                table: "device_transfers",
                column: "receiver_rejected_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_equipment_department_id",
                table: "equipment_settings",
                column: "equipment_department_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_updated_by_account_id",
                table: "equipment_settings",
                column: "updated_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_history_actor_account_id",
                table: "equipment_settings_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_history_department_id",
                table: "equipment_settings_history",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_history_occurred_at",
                table: "equipment_settings_history",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_history_previous_department_id",
                table: "equipment_settings_history",
                column: "previous_department_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_settings_history_request_id",
                table: "equipment_settings_history",
                column: "request_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_device_transfers_accounts_receiver_confirmed_account_id",
                table: "device_transfers",
                column: "receiver_confirmed_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_device_transfers_accounts_receiver_rejected_account_id",
                table: "device_transfers",
                column: "receiver_rejected_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_transfers_accounts_receiver_confirmed_account_id",
                table: "device_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_device_transfers_accounts_receiver_rejected_account_id",
                table: "device_transfers");

            migrationBuilder.DropTable(
                name: "equipment_settings");

            migrationBuilder.DropTable(
                name: "equipment_settings_history");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_receiver_confirmed_account_id",
                table: "device_transfers");

            migrationBuilder.DropIndex(
                name: "IX_device_transfers_receiver_rejected_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "receiver_confirmed_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "receiver_confirmed_at",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "receiver_rejected_account_id",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "receiver_rejected_at",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "receiver_rejection_reason",
                table: "device_transfers");

            migrationBuilder.DropColumn(
                name: "transfer_type",
                table: "device_transfers");

            // PostgreSQL cannot remove a label with ALTER TYPE. Recreate the enum on downgrade
            // so rolling back and reapplying this migration remains supported.
            migrationBuilder.Sql("""
                UPDATE device_transfers SET status = 'PENDING_APPROVAL'
                WHERE status::text = 'PENDING_CONFIRMATION';
                ALTER TYPE device_transfer_status RENAME TO device_transfer_status_with_confirmation;
                CREATE TYPE device_transfer_status AS ENUM ('PENDING_APPROVAL', 'REJECTED', 'APPROVED', 'COMPLETED', 'CANCELLED');
                ALTER TABLE device_transfers ALTER COLUMN status DROP DEFAULT;
                ALTER TABLE device_transfers ALTER COLUMN status TYPE device_transfer_status
                    USING status::text::device_transfer_status;
                ALTER TABLE device_transfers ALTER COLUMN status SET DEFAULT 'PENDING_APPROVAL';
                DROP TYPE device_transfer_status_with_confirmation;
                """);
        }
    }
}
