using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceTransfers;
using CSVC.Infrastructure.Persistence;

namespace CSVC.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CsvcDbContext))]
[Migration("20261029120000_InventoryFoundation")]
public sealed class InventoryFoundation : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.13")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "audit_outcome", new[] { "DENIED", "FAILED", "NO_CHANGE", "SUCCESS" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "entity_status", new[] { "ACTIVE", "LOCKED" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "device_condition", new[] { "DAMAGED", "GOOD", "MISSING" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "device_code_source", new[] { "GENERATED", "MANUAL" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "device_receipt_status", new[] { "APPROVED", "CANCELLED", "COMPLETED", "PENDING_APPROVAL", "REJECTED" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "device_loan_status", new[] { "APPROVED", "CANCELLED", "ON_LOAN", "PENDING_APPROVAL", "REJECTED", "RETURNED" });
        NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "device_transfer_status", new[] { "APPROVED", "CANCELLED", "COMPLETED", "PENDING_APPROVAL", "REJECTED" });
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
        modelBuilder.HasSequence<long>("account_code_seq");
        modelBuilder.HasSequence<long>("department_code_seq");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CsvcDbContext).Assembly);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE TYPE device_condition AS ENUM ('GOOD', 'DAMAGED', 'MISSING')");
        migrationBuilder.Sql("CREATE TYPE device_code_source AS ENUM ('MANUAL', 'GENERATED')");
        migrationBuilder.Sql("CREATE TYPE device_receipt_status AS ENUM ('PENDING_APPROVAL', 'REJECTED', 'APPROVED', 'COMPLETED', 'CANCELLED')");
        migrationBuilder.Sql("CREATE TYPE device_loan_status AS ENUM ('PENDING_APPROVAL', 'REJECTED', 'APPROVED', 'ON_LOAN', 'RETURNED', 'CANCELLED')");
        migrationBuilder.Sql("CREATE TYPE device_transfer_status AS ENUM ('PENDING_APPROVAL', 'REJECTED', 'APPROVED', 'COMPLETED', 'CANCELLED')");

        migrationBuilder.Sql("""
            CREATE TABLE device_identities (
                device_code varchar(32) PRIMARY KEY,
                barcode varchar(32) NOT NULL,
                reserved_at timestamptz NOT NULL,
                CONSTRAINT uq_device_identities_barcode UNIQUE (barcode),
                CONSTRAINT ck_device_identities_device_code CHECK (btrim(device_code) <> ''),
                CONSTRAINT ck_device_identities_barcode CHECK (btrim(barcode) <> '')
            )
            """);

        migrationBuilder.Sql("""
            CREATE TABLE devices (
                id uuid PRIMARY KEY,
                device_code varchar(32) NOT NULL,
                normalized_device_code text GENERATED ALWAYS AS (upper(btrim(device_code))) STORED NOT NULL,
                barcode varchar(32) NOT NULL,
                name varchar(200) NOT NULL,
                category_id uuid NOT NULL REFERENCES device_categories(id) ON DELETE RESTRICT,
                manufacturer_id uuid NULL REFERENCES manufacturers(id) ON DELETE RESTRICT,
                model varchar(120) NULL,
                serial_number varchar(100) NULL,
                purchase_date date NULL,
                paid_invoice_code varchar(64) NULL,
                purchase_price numeric(18,2) NULL,
                warranty_expiry_date date NULL,
                department_id uuid NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                area_id uuid NULL REFERENCES areas(id) ON DELETE RESTRICT,
                responsible_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                status entity_status NOT NULL DEFAULT 'ACTIVE',
                condition device_condition NOT NULL DEFAULT 'GOOD',
                notes text NULL,
                version integer NOT NULL DEFAULT 1,
                CONSTRAINT fk_devices_identity_code FOREIGN KEY (device_code) REFERENCES device_identities(device_code) ON DELETE RESTRICT,
                CONSTRAINT fk_devices_identity_barcode FOREIGN KEY (barcode) REFERENCES device_identities(barcode) ON DELETE RESTRICT,
                CONSTRAINT ck_devices_device_code CHECK (btrim(device_code) <> ''),
                CONSTRAINT ck_devices_barcode CHECK (btrim(barcode) <> ''),
                CONSTRAINT ck_devices_name CHECK (btrim(name) <> ''),
                CONSTRAINT ck_devices_purchase_price CHECK (purchase_price IS NULL OR purchase_price >= 0)
            )
            """);
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_devices_normalized_device_code ON devices(normalized_device_code)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_devices_barcode ON devices(barcode)");
        migrationBuilder.Sql("CREATE INDEX \"IX_devices_device_code\" ON devices(device_code)");
        migrationBuilder.Sql("CREATE INDEX ix_devices_department_status ON devices(department_id, status)");
        migrationBuilder.Sql("CREATE INDEX ix_devices_category ON devices(category_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_devices_manufacturer_id\" ON devices(manufacturer_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_devices_area_id\" ON devices(area_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_devices_responsible_account_id\" ON devices(responsible_account_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_receipts (
                id uuid PRIMARY KEY,
                receipt_code varchar(32) NOT NULL,
                supplier_id uuid NOT NULL REFERENCES suppliers(id) ON DELETE RESTRICT,
                carrier_name varchar(200) NULL,
                request_code varchar(64) NULL,
                delivery_date date NOT NULL,
                delivery_batch varchar(100) NOT NULL,
                requesting_department_id uuid NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                paid_invoice_code varchar(64) NULL,
                creator_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL,
                status device_receipt_status NOT NULL DEFAULT 'PENDING_APPROVAL',
                decision_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                decided_at timestamptz NULL,
                completed_at timestamptz NULL,
                decision_reason varchar(1000) NULL,
                notes text NULL,
                version integer NOT NULL DEFAULT 1,
                CONSTRAINT ck_device_receipts_receipt_code CHECK (btrim(receipt_code) <> ''),
                CONSTRAINT ck_device_receipts_delivery_batch CHECK (btrim(delivery_batch) <> '')
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_receipts_status_created ON device_receipts(status, created_at)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_receipts_code ON device_receipts(receipt_code)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipts_supplier_id\" ON device_receipts(supplier_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipts_requesting_department_id\" ON device_receipts(requesting_department_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipts_creator_account_id\" ON device_receipts(creator_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipts_decision_account_id\" ON device_receipts(decision_account_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_receipt_items (
                id uuid PRIMARY KEY,
                receipt_id uuid NOT NULL REFERENCES device_receipts(id) ON DELETE RESTRICT,
                category_id uuid NOT NULL REFERENCES device_categories(id) ON DELETE RESTRICT,
                name varchar(200) NOT NULL,
                device_code varchar(32) NOT NULL,
                barcode varchar(32) NOT NULL,
                device_code_source device_code_source NOT NULL,
                serial_number varchar(100) NULL,
                device_id uuid NULL REFERENCES devices(id) ON DELETE RESTRICT,
                CONSTRAINT fk_device_receipt_items_identity_code FOREIGN KEY (device_code) REFERENCES device_identities(device_code) ON DELETE RESTRICT,
                CONSTRAINT fk_device_receipt_items_identity_barcode FOREIGN KEY (barcode) REFERENCES device_identities(barcode) ON DELETE RESTRICT,
                CONSTRAINT ck_device_receipt_items_name CHECK (btrim(name) <> ''),
                CONSTRAINT ck_device_receipt_items_device_code CHECK (btrim(device_code) <> ''),
                CONSTRAINT ck_device_receipt_items_barcode CHECK (btrim(barcode) <> '')
            )
            """);
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipt_items_receipt_id\" ON device_receipt_items(receipt_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_receipt_items_category_id\" ON device_receipt_items(category_id)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_receipt_items_device_code ON device_receipt_items(device_code)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_receipt_items_barcode ON device_receipt_items(barcode)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_receipt_items_device_id ON device_receipt_items(device_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_loans (
                id uuid PRIMARY KEY,
                loan_code varchar(32) NOT NULL,
                borrower_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                borrower_department_id uuid NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                purpose varchar(1000) NOT NULL,
                due_at timestamptz NOT NULL,
                creator_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL,
                status device_loan_status NOT NULL DEFAULT 'PENDING_APPROVAL',
                decision_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                decided_at timestamptz NULL,
                handed_over_at timestamptz NULL,
                handover_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                returned_at timestamptz NULL,
                return_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                decision_reason varchar(1000) NULL,
                notes text NULL,
                version integer NOT NULL DEFAULT 1,
                CONSTRAINT ck_device_loans_due_at CHECK (due_at > created_at)
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_loans_status_due ON device_loans(status, due_at)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_loans_code ON device_loans(loan_code)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_borrower_account_id\" ON device_loans(borrower_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_borrower_department_id\" ON device_loans(borrower_department_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_creator_account_id\" ON device_loans(creator_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_decision_account_id\" ON device_loans(decision_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_handover_account_id\" ON device_loans(handover_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_loans_return_account_id\" ON device_loans(return_account_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_loan_items (
                id uuid PRIMARY KEY,
                loan_id uuid NOT NULL REFERENCES device_loans(id) ON DELETE RESTRICT,
                device_id uuid NOT NULL REFERENCES devices(id) ON DELETE RESTRICT,
                condition_at_handover device_condition NULL,
                handover_note varchar(1000) NULL,
                condition_at_return device_condition NULL,
                return_note varchar(1000) NULL,
                CONSTRAINT ck_device_loan_items_id CHECK (id IS NOT NULL)
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_loan_items_device ON device_loan_items(device_id)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_loan_items_loan_device ON device_loan_items(loan_id, device_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_transfers (
                id uuid PRIMARY KEY,
                transfer_code varchar(32) NOT NULL,
                source_department_id uuid NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                target_department_id uuid NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                target_responsible_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                reason varchar(1000) NOT NULL,
                creator_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL,
                status device_transfer_status NOT NULL DEFAULT 'PENDING_APPROVAL',
                decision_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                decided_at timestamptz NULL,
                source_handover_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                target_handover_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                completed_at timestamptz NULL,
                decision_reason varchar(1000) NULL,
                notes text NULL,
                version integer NOT NULL DEFAULT 1,
                CONSTRAINT ck_device_transfers_distinct_departments CHECK (source_department_id <> target_department_id),
                CONSTRAINT ck_device_transfers_reason CHECK (btrim(reason) <> '')
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_transfers_status_created ON device_transfers(status, created_at)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_transfers_code ON device_transfers(transfer_code)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_source_department_id\" ON device_transfers(source_department_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_target_department_id\" ON device_transfers(target_department_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_target_responsible_account_id\" ON device_transfers(target_responsible_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_creator_account_id\" ON device_transfers(creator_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_decision_account_id\" ON device_transfers(decision_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_source_handover_account_id\" ON device_transfers(source_handover_account_id)");
        migrationBuilder.Sql("CREATE INDEX \"IX_device_transfers_target_handover_account_id\" ON device_transfers(target_handover_account_id)");

        migrationBuilder.Sql("""
            CREATE TABLE device_transfer_items (
                id uuid PRIMARY KEY,
                transfer_id uuid NOT NULL REFERENCES device_transfers(id) ON DELETE RESTRICT,
                device_id uuid NOT NULL REFERENCES devices(id) ON DELETE RESTRICT,
                condition_at_handover device_condition NULL,
                handover_note varchar(1000) NULL,
                CONSTRAINT ck_device_transfer_items_id CHECK (id IS NOT NULL)
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_transfer_items_device ON device_transfer_items(device_id)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_transfer_items_transfer_device ON device_transfer_items(transfer_id, device_id)");

        CreateHistoryTable(migrationBuilder, "device_history", "device_id", "devices");
        CreateHistoryTable(migrationBuilder, "device_receipt_history", "receipt_id", "device_receipts");
        CreateHistoryTable(migrationBuilder, "device_loan_history", "loan_id", "device_loans");
        CreateHistoryTable(migrationBuilder, "device_transfer_history", "transfer_id", "device_transfers");

        migrationBuilder.Sql("""
            CREATE TABLE device_operation_holds (
                device_code varchar(32) PRIMARY KEY,
                device_id uuid NULL REFERENCES devices(id) ON DELETE RESTRICT,
                operation_kind varchar(16) NOT NULL,
                operation_id uuid NOT NULL,
                held_at timestamptz NOT NULL,
                CONSTRAINT ck_device_operation_holds_device_code CHECK (btrim(device_code) <> '')
            )
            """);
        migrationBuilder.Sql("CREATE INDEX ix_device_operation_holds_operation ON device_operation_holds(operation_kind, operation_id)");
        migrationBuilder.Sql("CREATE UNIQUE INDEX uq_device_operation_holds_device_id ON device_operation_holds(device_id) WHERE device_id IS NOT NULL");

        RenameForeignKeys(migrationBuilder);
    }

    private static void CreateHistoryTable(MigrationBuilder migrationBuilder, string table, string targetColumn, string targetTable)
    {
        migrationBuilder.Sql($"""
            CREATE TABLE {table} (
                id uuid PRIMARY KEY,
                request_id uuid NOT NULL,
                actor_account_id uuid NULL REFERENCES accounts(id) ON DELETE RESTRICT,
                action_code varchar(64) NOT NULL,
                outcome audit_outcome NOT NULL,
                reason_code varchar(80) NULL,
                occurred_at timestamptz NOT NULL,
                {targetColumn} uuid NULL REFERENCES {targetTable}(id) ON DELETE RESTRICT,
                changed_fields jsonb NULL,
                before_data jsonb NULL,
                after_data jsonb NULL
            )
            """);
        migrationBuilder.Sql($"CREATE UNIQUE INDEX uq_{table}_request_action ON {table}(request_id, action_code)");
        migrationBuilder.Sql($"CREATE INDEX ix_{table}_target ON {table}({targetColumn}, occurred_at)");
        migrationBuilder.Sql($"CREATE INDEX \"IX_{table}_actor_account_id\" ON {table}(actor_account_id)");
    }

    private static void RenameForeignKeys(MigrationBuilder migrationBuilder)
    {
        (string Table, string Old, string New)[] constraints =
        [
            ("devices", "fk_devices_identity_code", "FK_devices_device_identities_device_code"),
            ("devices", "fk_devices_identity_barcode", "FK_devices_device_identities_barcode"),
            ("devices", "devices_category_id_fkey", "FK_devices_device_categories_category_id"),
            ("devices", "devices_manufacturer_id_fkey", "FK_devices_manufacturers_manufacturer_id"),
            ("devices", "devices_department_id_fkey", "FK_devices_departments_department_id"),
            ("devices", "devices_area_id_fkey", "FK_devices_areas_area_id"),
            ("devices", "devices_responsible_account_id_fkey", "FK_devices_accounts_responsible_account_id"),
            ("device_operation_holds", "device_operation_holds_device_id_fkey", "FK_device_operation_holds_devices_device_id"),
            ("device_receipts", "device_receipts_supplier_id_fkey", "FK_device_receipts_suppliers_supplier_id"),
            ("device_receipts", "device_receipts_requesting_department_id_fkey", "FK_device_receipts_departments_requesting_department_id"),
            ("device_receipts", "device_receipts_creator_account_id_fkey", "FK_device_receipts_accounts_creator_account_id"),
            ("device_receipts", "device_receipts_decision_account_id_fkey", "FK_device_receipts_accounts_decision_account_id"),
            ("device_receipt_items", "fk_device_receipt_items_identity_code", "FK_device_receipt_items_device_identities_device_code"),
            ("device_receipt_items", "fk_device_receipt_items_identity_barcode", "FK_device_receipt_items_device_identities_barcode"),
            ("device_receipt_items", "device_receipt_items_receipt_id_fkey", "FK_device_receipt_items_device_receipts_receipt_id"),
            ("device_receipt_items", "device_receipt_items_category_id_fkey", "FK_device_receipt_items_device_categories_category_id"),
            ("device_receipt_items", "device_receipt_items_device_id_fkey", "FK_device_receipt_items_devices_device_id"),
            ("device_loans", "device_loans_borrower_account_id_fkey", "FK_device_loans_accounts_borrower_account_id"),
            ("device_loans", "device_loans_borrower_department_id_fkey", "FK_device_loans_departments_borrower_department_id"),
            ("device_loans", "device_loans_creator_account_id_fkey", "FK_device_loans_accounts_creator_account_id"),
            ("device_loans", "device_loans_decision_account_id_fkey", "FK_device_loans_accounts_decision_account_id"),
            ("device_loans", "device_loans_handover_account_id_fkey", "FK_device_loans_accounts_handover_account_id"),
            ("device_loans", "device_loans_return_account_id_fkey", "FK_device_loans_accounts_return_account_id"),
            ("device_loan_items", "device_loan_items_loan_id_fkey", "FK_device_loan_items_device_loans_loan_id"),
            ("device_loan_items", "device_loan_items_device_id_fkey", "FK_device_loan_items_devices_device_id"),
            ("device_transfers", "device_transfers_source_department_id_fkey", "FK_device_transfers_departments_source_department_id"),
            ("device_transfers", "device_transfers_target_department_id_fkey", "FK_device_transfers_departments_target_department_id"),
            ("device_transfers", "device_transfers_target_responsible_account_id_fkey", "FK_device_transfers_accounts_target_responsible_account_id"),
            ("device_transfers", "device_transfers_creator_account_id_fkey", "FK_device_transfers_accounts_creator_account_id"),
            ("device_transfers", "device_transfers_decision_account_id_fkey", "FK_device_transfers_accounts_decision_account_id"),
            ("device_transfers", "device_transfers_source_handover_account_id_fkey", "FK_device_transfers_accounts_source_handover_account_id"),
            ("device_transfers", "device_transfers_target_handover_account_id_fkey", "FK_device_transfers_accounts_target_handover_account_id"),
            ("device_transfer_items", "device_transfer_items_transfer_id_fkey", "FK_device_transfer_items_device_transfers_transfer_id"),
            ("device_transfer_items", "device_transfer_items_device_id_fkey", "FK_device_transfer_items_devices_device_id"),
            ("device_history", "device_history_actor_account_id_fkey", "FK_device_history_accounts_actor_account_id"),
            ("device_history", "device_history_device_id_fkey", "FK_device_history_devices_device_id"),
            ("device_receipt_history", "device_receipt_history_actor_account_id_fkey", "FK_device_receipt_history_accounts_actor_account_id"),
            ("device_receipt_history", "device_receipt_history_receipt_id_fkey", "FK_device_receipt_history_device_receipts_receipt_id"),
            ("device_loan_history", "device_loan_history_actor_account_id_fkey", "FK_device_loan_history_accounts_actor_account_id"),
            ("device_loan_history", "device_loan_history_loan_id_fkey", "FK_device_loan_history_device_loans_loan_id"),
            ("device_transfer_history", "device_transfer_history_actor_account_id_fkey", "FK_device_transfer_history_accounts_actor_account_id"),
            ("device_transfer_history", "device_transfer_history_transfer_id_fkey", "FK_device_transfer_history_device_transfers_transfer_id")
        ];

        foreach (var (table, oldName, newName) in constraints)
            migrationBuilder.Sql($"ALTER TABLE {table} RENAME CONSTRAINT \"{oldName}\" TO \"{newName}\"");

        migrationBuilder.Sql("CREATE INDEX ix_device_history_occurred_at ON device_history (occurred_at)");
        migrationBuilder.Sql("CREATE INDEX ix_device_receipt_history_occurred_at ON device_receipt_history (occurred_at)");
        migrationBuilder.Sql("CREATE INDEX ix_device_loan_history_occurred_at ON device_loan_history (occurred_at)");
        migrationBuilder.Sql("CREATE INDEX ix_device_transfer_history_occurred_at ON device_transfer_history (occurred_at)");
        migrationBuilder.Sql("CREATE INDEX ix_devices_manufacturer ON devices (manufacturer_id)");
        migrationBuilder.Sql("CREATE INDEX ix_devices_area ON devices (area_id)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE device_operation_holds");
        migrationBuilder.Sql("DROP TABLE device_transfer_history");
        migrationBuilder.Sql("DROP TABLE device_loan_history");
        migrationBuilder.Sql("DROP TABLE device_receipt_history");
        migrationBuilder.Sql("DROP TABLE device_history");
        migrationBuilder.Sql("DROP TABLE device_transfer_items");
        migrationBuilder.Sql("DROP TABLE device_transfers");
        migrationBuilder.Sql("DROP TABLE device_loan_items");
        migrationBuilder.Sql("DROP TABLE device_loans");
        migrationBuilder.Sql("DROP TABLE device_receipt_items");
        migrationBuilder.Sql("DROP TABLE device_receipts");
        migrationBuilder.Sql("DROP TABLE devices");
        migrationBuilder.Sql("DROP TABLE device_identities");
        migrationBuilder.Sql("DROP TYPE device_transfer_status");
        migrationBuilder.Sql("DROP TYPE device_loan_status");
        migrationBuilder.Sql("DROP TYPE device_receipt_status");
        migrationBuilder.Sql("DROP TYPE device_code_source");
        migrationBuilder.Sql("DROP TYPE device_condition");
    }
}
