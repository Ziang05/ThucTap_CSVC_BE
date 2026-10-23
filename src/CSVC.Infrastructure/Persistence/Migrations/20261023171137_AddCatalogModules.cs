using System;
using CSVC.Core.Common;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<EntityStatus>(type: "entity_status", nullable: false, defaultValue: EntityStatus.Active),
                    normalized_display_code = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(display_code))", stored: true),
                    normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_categories", x => x.id);
                    table.CheckConstraint("ck_device_categories_display_code", "btrim(display_code) <> ''");
                    table.CheckConstraint("ck_device_categories_name", "normalized_name <> ''");
                });

            migrationBuilder.CreateTable(
                name: "manufacturers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    website = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    short_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    contact_info = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<EntityStatus>(type: "entity_status", nullable: false, defaultValue: EntityStatus.Active),
                    normalized_display_code = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(display_code))", stored: true),
                    normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.id);
                    table.CheckConstraint("ck_manufacturers_display_code", "btrim(display_code) <> ''");
                    table.CheckConstraint("ck_manufacturers_name", "normalized_name <> ''");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    tax_code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<EntityStatus>(type: "entity_status", nullable: false, defaultValue: EntityStatus.Active),
                    normalized_display_code = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(display_code))", stored: true),
                    normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_display_code", "btrim(display_code) <> ''");
                    table.CheckConstraint("ck_suppliers_name", "normalized_name <> ''");
                });

            migrationBuilder.CreateTable(
                name: "category_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_category_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_category_history_device_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "device_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manufacturer_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturer_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_manufacturer_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_manufacturer_history_manufacturers_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalTable: "manufacturers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                    reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    before_data = table.Column<string>(type: "jsonb", nullable: true),
                    after_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_history_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_category_history_actor_account_id",
                table: "category_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_history_target",
                table: "category_history",
                columns: new[] { "category_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "uq_category_history_request_action",
                table: "category_history",
                columns: new[] { "request_id", "action_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_categories_display_code",
                table: "device_categories",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_device_categories_normalized_display_code",
                table: "device_categories",
                column: "normalized_display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_device_categories_normalized_name",
                table: "device_categories",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturer_history_actor_account_id",
                table: "manufacturer_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_history_target",
                table: "manufacturer_history",
                columns: new[] { "manufacturer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "uq_manufacturer_history_request_action",
                table: "manufacturer_history",
                columns: new[] { "request_id", "action_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_display_code",
                table: "manufacturers",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_manufacturers_normalized_display_code",
                table: "manufacturers",
                column: "normalized_display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_manufacturers_normalized_name",
                table: "manufacturers",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_history_actor_account_id",
                table: "supplier_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_history_target",
                table: "supplier_history",
                columns: new[] { "supplier_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "uq_supplier_history_request_action",
                table: "supplier_history",
                columns: new[] { "request_id", "action_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_display_code",
                table: "suppliers",
                column: "display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_suppliers_normalized_display_code",
                table: "suppliers",
                column: "normalized_display_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_suppliers_normalized_name",
                table: "suppliers",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_history");

            migrationBuilder.DropTable(
                name: "manufacturer_history");

            migrationBuilder.DropTable(
                name: "supplier_history");

            migrationBuilder.DropTable(
                name: "device_categories");

            migrationBuilder.DropTable(
                name: "manufacturers");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
