using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaterialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "equipment_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    before_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    after_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_changes", x => x.id);
                    table.CheckConstraint("ck_equipment_change_time", "performed_at <= recorded_at AND (before_value IS NOT NULL OR after_value IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_equipment_changes_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_changes_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_changes_equipment_changes_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "equipment_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_changes_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_changes_work_tasks_work_task_id",
                        column: x => x.work_task_id,
                        principalTable: "work_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_catalog_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_catalog_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_material_catalog_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_work_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procurement_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_responsible_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_documents", x => x.id);
                    table.CheckConstraint("ck_material_document_posting", "version > 0 AND ((posted_at IS NULL AND posted_by_account_id IS NULL) OR (posted_at IS NOT NULL AND posted_by_account_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_material_documents_accounts_approved_by_account_id",
                        column: x => x.approved_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_accounts_posted_by_account_id",
                        column: x => x.posted_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_accounts_received_by_account_id",
                        column: x => x.received_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_accounts_target_responsible_account_id",
                        column: x => x.target_responsible_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_departments_target_department_id",
                        column: x => x.target_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_procurement_requests_procurement_request~",
                        column: x => x.procurement_request_id,
                        principalTable: "procurement_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_documents_work_tasks_parent_work_task_id",
                        column: x => x.parent_work_task_id,
                        principalTable: "work_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_units", x => x.id);
                    table.CheckConstraint("ck_material_unit_scale", "decimal_places BETWEEN 0 AND 3 AND version > 0");
                });

            migrationBuilder.CreateTable(
                name: "material_warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_warehouses", x => x.id);
                    table.ForeignKey(
                        name: "FK_material_warehouses_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_document_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_document_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_material_document_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_history_material_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "material_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_materials_material_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "material_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_locations", x => x.id);
                    table.ForeignKey(
                        name: "FK_material_locations_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_locations_material_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "material_warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment_removed_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reusable = table.Column<bool>(type: "boolean", nullable: false),
                    condition_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_or_evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    lot_or_serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_removed_materials", x => x.id);
                    table.CheckConstraint("ck_equipment_removed_return", "quantity > 0 AND (disposition <> 'ReturnToStock' OR (reusable AND material_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_equipment_removed_materials_equipment_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "equipment_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_removed_materials_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_balances", x => x.id);
                    table.CheckConstraint("ck_material_balance_nonnegative", "quantity >= 0 AND version > 0");
                    table.ForeignKey(
                        name: "FK_material_balances_material_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "material_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_balances_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    condition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    condition_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_issue_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_line_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    lot_or_serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    material_code_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    material_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_code_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_document_lines", x => x.id);
                    table.UniqueConstraint("AK_material_document_lines_id_document_id", x => new { x.id, x.document_id });
                    table.CheckConstraint("ck_material_line_quantity", "quantity <> 0 AND line_no > 0");
                    table.ForeignKey(
                        name: "FK_material_document_lines_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_equipment_removed_materials_removed~",
                        column: x => x.removed_material_id,
                        principalTable: "equipment_removed_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_material_document_lines_original_is~",
                        column: x => x.original_issue_line_id,
                        principalTable: "material_document_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_material_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "material_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_material_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "material_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_material_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "material_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_document_lines_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment_material_uses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issue_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    lot_or_serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_material_uses", x => x.id);
                    table.CheckConstraint("ck_equipment_use_source", "quantity > 0 AND ((source = 'Warehouse' AND issue_line_id IS NOT NULL) OR (source <> 'Warehouse' AND issue_line_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_equipment_material_uses_equipment_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "equipment_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_material_uses_material_document_lines_issue_line_~",
                        column: x => x.issue_line_id,
                        principalTable: "material_document_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_material_uses_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leg = table.Column<int>(type: "integer", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_ledger", x => x.id);
                    table.CheckConstraint("ck_material_ledger_values", "delta <> 0 AND balance_after >= 0 AND leg IN (0,1)");
                    table.ForeignKey(
                        name: "FK_material_ledger_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_ledger_material_document_lines_line_id_document_id",
                        columns: x => new { x.line_id, x.document_id },
                        principalTable: "material_document_lines",
                        principalColumns: new[] { "id", "document_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_ledger_material_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "material_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_ledger_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_work_uses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_work_uses", x => x.id);
                    table.CheckConstraint("ck_material_work_use_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "FK_material_work_uses_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_work_uses_material_document_lines_issue_line_id",
                        column: x => x.issue_line_id,
                        principalTable: "material_document_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_work_uses_material_work_uses_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "material_work_uses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_work_uses_work_tasks_work_task_id",
                        column: x => x.work_task_id,
                        principalTable: "work_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_code",
                table: "equipment_changes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_device_id_performed_at",
                table: "equipment_changes",
                columns: new[] { "device_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_recorded_by_account_id",
                table: "equipment_changes",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_replaces_id",
                table: "equipment_changes",
                column: "replaces_id",
                unique: true,
                filter: "replaces_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_request_id",
                table: "equipment_changes",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_service_order_id",
                table: "equipment_changes",
                column: "service_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_work_task_id",
                table: "equipment_changes",
                column: "work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_material_uses_change_id",
                table: "equipment_material_uses",
                column: "change_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_material_uses_issue_line_id",
                table: "equipment_material_uses",
                column: "issue_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_material_uses_material_id",
                table: "equipment_material_uses",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_removed_materials_change_id",
                table: "equipment_removed_materials",
                column: "change_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_removed_materials_material_id",
                table: "equipment_removed_materials",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_balances_location_id",
                table: "material_balances",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_balances_material_id_location_id",
                table: "material_balances",
                columns: new[] { "material_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_catalog_history_actor_account_id",
                table: "material_catalog_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_catalog_history_catalog_target_id_occurred_at",
                table: "material_catalog_history",
                columns: new[] { "catalog", "target_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_document_history_actor_account_id",
                table: "material_document_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_history_document_id_occurred_at",
                table: "material_document_history",
                columns: new[] { "document_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_document_history_request_id",
                table: "material_document_history",
                column: "request_id",
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_device_id",
                table: "material_document_lines",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_document_id_line_no",
                table: "material_document_lines",
                columns: new[] { "document_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_location_id",
                table: "material_document_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_material_id",
                table: "material_document_lines",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_original_issue_line_id",
                table: "material_document_lines",
                column: "original_issue_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_removed_material_id",
                table: "material_document_lines",
                column: "removed_material_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_document_lines_to_location_id",
                table: "material_document_lines",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_approved_by_account_id",
                table: "material_documents",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_code",
                table: "material_documents",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_create_request_id",
                table: "material_documents",
                column: "create_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_created_by_account_id",
                table: "material_documents",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_department_id_status_created_at",
                table: "material_documents",
                columns: new[] { "department_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_parent_work_task_id",
                table: "material_documents",
                column: "parent_work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_posted_by_account_id",
                table: "material_documents",
                column: "posted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_procurement_request_id_kind",
                table: "material_documents",
                columns: new[] { "procurement_request_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_received_by_account_id",
                table: "material_documents",
                column: "received_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_service_order_id",
                table: "material_documents",
                column: "service_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_supplier_id",
                table: "material_documents",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_target_department_id",
                table: "material_documents",
                column: "target_department_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_documents_target_responsible_account_id",
                table: "material_documents",
                column: "target_responsible_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_ledger_actor_account_id",
                table: "material_ledger",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_ledger_line_id_document_id",
                table: "material_ledger",
                columns: new[] { "line_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_material_ledger_line_id_leg",
                table: "material_ledger",
                columns: new[] { "line_id", "leg" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_ledger_location_id",
                table: "material_ledger",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_ledger_material_id_location_id_occurred_at",
                table: "material_ledger",
                columns: new[] { "material_id", "location_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_locations_area_id",
                table: "material_locations",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_locations_code",
                table: "material_locations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_locations_warehouse_id",
                table: "material_locations",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_units_code",
                table: "material_units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_warehouses_code",
                table: "material_warehouses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_warehouses_department_id",
                table: "material_warehouses",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_actor_account_id",
                table: "material_work_uses",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_code",
                table: "material_work_uses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_issue_line_id",
                table: "material_work_uses",
                column: "issue_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_replaces_id",
                table: "material_work_uses",
                column: "replaces_id",
                unique: true,
                filter: "replaces_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_request_id",
                table: "material_work_uses",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_work_uses_work_task_id",
                table: "material_work_uses",
                column: "work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_code",
                table: "materials",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_materials_unit_id",
                table: "materials",
                column: "unit_id");
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_material_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Material ledger and evidence are append only' USING ERRCODE = '23514'; END $$;
                DO $$ DECLARE n text; BEGIN
                  FOREACH n IN ARRAY ARRAY['material_ledger','material_document_history','material_catalog_history','equipment_changes','equipment_material_uses','equipment_removed_materials','material_work_uses'] LOOP
                    EXECUTE format('CREATE TRIGGER immutable_material_evidence BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION protect_material_append_only()', n);
                  END LOOP;
                END $$;
                CREATE FUNCTION protect_posted_material_line() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM material_documents WHERE id = COALESCE(OLD.document_id, NEW.document_id) AND posted_at IS NOT NULL) THEN
                    RAISE EXCEPTION 'Posted material lines are immutable' USING ERRCODE = '23514';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER immutable_posted_material_line BEFORE INSERT OR UPDATE OR DELETE ON material_document_lines FOR EACH ROW EXECUTE FUNCTION protect_posted_material_line();
                CREATE FUNCTION protect_posted_material_document() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.posted_at IS NOT NULL THEN
                    IF TG_OP = 'DELETE' OR (to_jsonb(OLD) - ARRAY['status','received_by_account_id','received_at','version']) IS DISTINCT FROM (to_jsonb(NEW) - ARRAY['status','received_by_account_id','received_at','version'])
                       OR NOT (OLD.status = 'AwaitingReceipt' AND NEW.status = 'Completed' AND NEW.received_by_account_id IS NOT NULL AND NEW.received_at IS NOT NULL AND NEW.version = OLD.version + 1)
                    THEN RAISE EXCEPTION 'Posted material document is immutable except receipt confirmation' USING ERRCODE = '23514'; END IF;
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER immutable_posted_material_document BEFORE UPDATE OR DELETE ON material_documents FOR EACH ROW EXECUTE FUNCTION protect_posted_material_document();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS protect_material_append_only() CASCADE; DROP FUNCTION IF EXISTS protect_posted_material_line() CASCADE; DROP FUNCTION IF EXISTS protect_posted_material_document() CASCADE;");
            migrationBuilder.DropTable(
                name: "equipment_material_uses");

            migrationBuilder.DropTable(
                name: "material_balances");

            migrationBuilder.DropTable(
                name: "material_catalog_history");

            migrationBuilder.DropTable(
                name: "material_document_history");

            migrationBuilder.DropTable(
                name: "material_ledger");

            migrationBuilder.DropTable(
                name: "material_work_uses");

            migrationBuilder.DropTable(
                name: "material_document_lines");

            migrationBuilder.DropTable(
                name: "equipment_removed_materials");

            migrationBuilder.DropTable(
                name: "material_documents");

            migrationBuilder.DropTable(
                name: "material_locations");

            migrationBuilder.DropTable(
                name: "equipment_changes");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "material_warehouses");

            migrationBuilder.DropTable(
                name: "material_units");
        }
    }
}
