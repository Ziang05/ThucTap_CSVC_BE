using System;
using CSVC.Core.Common;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceOrdersAndExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    create_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canceled_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_orders", x => x.id);
                    table.CheckConstraint("ck_service_orders_cancel", "status <> 'Canceled' OR (canceled_at IS NOT NULL AND canceled_by_account_id IS NOT NULL AND COALESCE(btrim(cancel_reason), '') <> '')");
                    table.CheckConstraint("ck_service_orders_status", "status IN ('PendingApproval','InProgress','Completed','Canceled')");
                    table.CheckConstraint("ck_service_orders_type", "order_type IN ('Maintenance','Repair')");
                    table.ForeignKey(
                        name: "FK_service_orders_accounts_approved_by_account_id",
                        column: x => x.approved_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_orders_accounts_canceled_by_account_id",
                        column: x => x.canceled_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_orders_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_slips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    display_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facility_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    facility_address_snapshot = table.Column<string>(type: "text", nullable: true),
                    facility_contact_snapshot = table.Column<string>(type: "text", nullable: true),
                    planned_export_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    result_recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    recipient_contact = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    evidence_ref = table.Column<string>(type: "text", nullable: true),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_slips", x => x.id);
                    table.CheckConstraint("ck_export_slips_facility", "btrim(facility_name_snapshot) <> ''");
                    table.CheckConstraint("ck_export_slips_result", "(result IS NULL AND status <> 'Completed') OR (result IS NOT NULL AND status = 'Completed' AND completed_at IS NOT NULL AND result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND ((result = 'Success' AND COALESCE(btrim(recipient_name), '') <> '') OR (result = 'Failed' AND COALESCE(btrim(failure_reason), '') <> '')))");
                    table.CheckConstraint("ck_export_slips_sequence", "sequence_no > 0");
                    table.CheckConstraint("ck_export_slips_status", "status IN ('PendingApproval','InProgress','Completed','Canceled')");
                    table.ForeignKey(
                        name: "FK_export_slips_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_slips_accounts_result_recorded_by_account_id",
                        column: x => x.result_recorded_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_slips_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_no = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    execution_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    work_description = table.Column<string>(type: "text", nullable: true),
                    result_description = table.Column<string>(type: "text", nullable: true),
                    result_recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    device_code_snapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    serial_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_order_items", x => x.id);
                    table.CheckConstraint("ck_service_items_method", "execution_method IN ('OnSite','External')");
                    table.CheckConstraint("ck_service_items_no", "item_no > 0");
                    table.CheckConstraint("ck_service_items_result", "execution_status NOT IN ('Completed','Failed') OR (result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND COALESCE(btrim(result_description), '') <> '')");
                    table.CheckConstraint("ck_service_items_status", "execution_status IS NULL OR execution_status IN ('InProgress','NotExecuted','Completed','Failed')");
                    table.ForeignKey(
                        name: "FK_service_order_items_accounts_result_recorded_by_account_id",
                        column: x => x.result_recorded_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_items_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_items_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_export_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_history_export_slips_export_slip_id",
                        column: x => x.export_slip_id,
                        principalTable: "export_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export_slip_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_snapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    serial_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_slip_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_export_slip_items_export_slips_export_slip_id",
                        column: x => x.export_slip_id,
                        principalTable: "export_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_slip_items_service_order_items_service_order_item_id",
                        column: x => x.service_order_item_id,
                        principalTable: "service_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repair_item_details",
                columns: table => new
                {
                    service_order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    damage_report_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_item_details", x => x.service_order_item_id);
                    table.ForeignKey(
                        name: "FK_repair_item_details_service_order_items_service_order_item_~",
                        column: x => x.service_order_item_id,
                        principalTable: "service_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_order_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_export_slip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_order_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_order_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_history_export_slips_related_export_slip_id",
                        column: x => x.related_export_slip_id,
                        principalTable: "export_slips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_history_service_order_items_service_order_ite~",
                        column: x => x.service_order_item_id,
                        principalTable: "service_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_order_history_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_export_history_actor_account_id",
                table: "export_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_history_export_slip_id_occurred_at",
                table: "export_history",
                columns: new[] { "export_slip_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_history_request_id_action_code_export_slip_id",
                table: "export_history",
                columns: new[] { "request_id", "action_code", "export_slip_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_export_slip_id_service_order_item_id",
                table: "export_slip_items",
                columns: new[] { "export_slip_id", "service_order_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slip_items_service_order_item_id",
                table: "export_slip_items",
                column: "service_order_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_created_by_account_id",
                table: "export_slips",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_display_code",
                table: "export_slips",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_recorded_by_account_id",
                table: "export_slips",
                column: "result_recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_request_id",
                table: "export_slips",
                column: "result_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_result_result_recorded_at",
                table: "export_slips",
                columns: new[] { "result", "result_recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_service_order_id_sequence_no",
                table: "export_slips",
                columns: new[] { "service_order_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_slips_status_created_at",
                table: "export_slips",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_item_details_damage_report_id",
                table: "repair_item_details",
                column: "damage_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_history_actor_account_id",
                table: "service_order_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_history_related_export_slip_id_occurred_at",
                table: "service_order_history",
                columns: new[] { "related_export_slip_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_history_request_id_action_code_service_order_~",
                table: "service_order_history",
                columns: new[] { "request_id", "action_code", "service_order_id", "target_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_order_history_service_order_id_occurred_at",
                table: "service_order_history",
                columns: new[] { "service_order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_history_service_order_item_id_occurred_at",
                table: "service_order_history",
                columns: new[] { "service_order_item_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_device_id_execution_status",
                table: "service_order_items",
                columns: new[] { "device_id", "execution_status" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_result_recorded_by_account_id",
                table: "service_order_items",
                column: "result_recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_service_order_id_device_id",
                table: "service_order_items",
                columns: new[] { "service_order_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_service_order_id_execution_status",
                table: "service_order_items",
                columns: new[] { "service_order_id", "execution_status" });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_items_service_order_id_item_no",
                table: "service_order_items",
                columns: new[] { "service_order_id", "item_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_approval_request_id",
                table: "service_orders",
                column: "approval_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_approved_by_account_id",
                table: "service_orders",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_cancel_request_id",
                table: "service_orders",
                column: "cancel_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_canceled_by_account_id",
                table: "service_orders",
                column: "canceled_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_create_request_id",
                table: "service_orders",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_created_by_account_id_created_at",
                table: "service_orders",
                columns: new[] { "created_by_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_display_code",
                table: "service_orders",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_orders_order_type_status_created_at",
                table: "service_orders",
                columns: new[] { "order_type", "status", "created_at" });

            // Deferred checks allow EF to insert the aggregate in FK order, while rejecting
            // cross-order/onsite links even for writers outside the application service.
            migrationBuilder.Sql("""
                CREATE FUNCTION check_service_export_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE affected_item uuid; affected_slip uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'export_slip_items' THEN
                        affected_item := NEW.service_order_item_id; affected_slip := NEW.export_slip_id;
                    ELSIF TG_TABLE_NAME = 'service_order_items' THEN
                        affected_item := NEW.id;
                    ELSE
                        affected_slip := NEW.id;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM export_slip_items line
                        JOIN export_slips slip ON slip.id = line.export_slip_id
                        JOIN service_order_items item ON item.id = line.service_order_item_id
                        WHERE ((affected_item IS NOT NULL AND item.id = affected_item)
                            OR (affected_slip IS NOT NULL AND slip.id = affected_slip))
                          AND (item.service_order_id <> slip.service_order_id OR item.execution_method <> 'External')
                    ) THEN
                        RAISE EXCEPTION 'Export items must be external and belong to the source order'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_export_slip_source_membership';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER ck_export_line_membership AFTER INSERT OR UPDATE ON export_slip_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();
                CREATE CONSTRAINT TRIGGER ck_service_item_export_membership AFTER UPDATE ON service_order_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();
                CREATE CONSTRAINT TRIGGER ck_export_header_membership AFTER UPDATE ON export_slips
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_service_export_membership();

                CREATE FUNCTION check_repair_item_type() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE affected_item uuid; affected_order uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'repair_item_details' THEN affected_item := NEW.service_order_item_id;
                    ELSIF TG_TABLE_NAME = 'service_order_items' THEN affected_item := NEW.id;
                    ELSE affected_order := NEW.id;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM repair_item_details detail
                        JOIN service_order_items item ON item.id = detail.service_order_item_id
                        JOIN service_orders source ON source.id = item.service_order_id
                        WHERE (item.id = affected_item OR source.id = affected_order) AND source.order_type <> 'Repair'
                    ) THEN
                        RAISE EXCEPTION 'Repair detail requires a repair order'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_repair_item_order_type';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER ck_repair_detail_type AFTER INSERT OR UPDATE ON repair_item_details
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_repair_item_type();
                CREATE CONSTRAINT TRIGGER ck_repair_item_type AFTER UPDATE ON service_order_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_repair_item_type();
                CREATE CONSTRAINT TRIGGER ck_repair_order_type AFTER UPDATE ON service_orders
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_repair_item_type();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER ck_export_line_membership ON export_slip_items;
                DROP TRIGGER ck_service_item_export_membership ON service_order_items;
                DROP TRIGGER ck_export_header_membership ON export_slips;
                DROP FUNCTION check_service_export_membership();
                DROP TRIGGER ck_repair_detail_type ON repair_item_details;
                DROP TRIGGER ck_repair_item_type ON service_order_items;
                DROP TRIGGER ck_repair_order_type ON service_orders;
                DROP FUNCTION check_repair_item_type();
                """);

            migrationBuilder.DropTable(
                name: "export_history");

            migrationBuilder.DropTable(
                name: "export_slip_items");

            migrationBuilder.DropTable(
                name: "repair_item_details");

            migrationBuilder.DropTable(
                name: "service_order_history");

            migrationBuilder.DropTable(
                name: "export_slips");

            migrationBuilder.DropTable(
                name: "service_order_items");

            migrationBuilder.DropTable(
                name: "service_orders");
        }
    }
}
