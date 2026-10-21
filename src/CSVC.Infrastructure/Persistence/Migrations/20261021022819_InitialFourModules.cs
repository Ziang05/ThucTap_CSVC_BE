using System;
using System.Net;
using CSVC.Core.Common;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CSVC.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialFourModules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:audit_outcome", "DENIED,FAILED,NO_CHANGE,SUCCESS")
            .Annotation("Npgsql:Enum:entity_status", "ACTIVE,LOCKED");

        migrationBuilder.CreateTable(
            name: "areas",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                type_label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                status = table.Column<EntityStatus>(type: "entity_status", nullable: false, defaultValue: EntityStatus.Active),
                normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_areas", x => x.id);
                table.CheckConstraint("ck_areas_code", "btrim(code) <> ''");
                table.CheckConstraint("ck_areas_name", "normalized_name <> ''");
                table.CheckConstraint("ck_areas_not_self_parent", "parent_id IS NULL OR parent_id <> id");
                table.ForeignKey(
                    name: "FK_areas_areas_parent_id",
                    column: x => x.parent_id,
                    principalTable: "areas",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "auth_login_throttles",
            columns: table => new
            {
                email_key = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                first_failed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                blocked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auth_login_throttles", x => x.email_key);
                table.CheckConstraint("ck_auth_login_throttles_email", "email_key <> '' AND email_key = lower(btrim(email_key)) AND email_key !~ '[[:space:]]'");
                table.CheckConstraint("ck_auth_login_throttles_failed_count", "failed_count >= 0");
            });

        migrationBuilder.CreateTable(
            name: "departments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                short_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                status = table.Column<EntityStatus>(type: "entity_status", nullable: false, defaultValue: EntityStatus.Active),
                normalized_name = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_departments", x => x.id);
                table.CheckConstraint("ck_departments_code", "btrim(code) <> ''");
                table.CheckConstraint("ck_departments_name", "normalized_name <> ''");
            });

        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                id = table.Column<short>(type: "smallint", nullable: false),
                code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                display_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "user_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                department_id = table.Column<Guid>(type: "uuid", nullable: false),
                full_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_profiles", x => x.id);
                table.ForeignKey(
                    name: "FK_user_profiles_departments_department_id",
                    column: x => x.department_id,
                    principalTable: "departments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                user_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<short>(type: "smallint", nullable: false),
                email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                is_lock = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                normalized_email = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(btrim(email))", stored: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounts", x => x.id);
                table.CheckConstraint("ck_accounts_code", "btrim(account_code) <> ''");
                table.CheckConstraint("ck_accounts_email", "normalized_email <> '' AND normalized_email !~ '[[:space:]]'");
                table.CheckConstraint("ck_accounts_password_hash", "btrim(password_hash) <> ''");
                table.ForeignKey(
                    name: "FK_accounts_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_accounts_user_profiles_user_profile_id",
                    column: x => x.user_profile_id,
                    principalTable: "user_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "account_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                target_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                before_data = table.Column<string>(type: "jsonb", nullable: true),
                after_data = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_account_history", x => x.id);
                table.ForeignKey(
                    name: "FK_account_history_accounts_actor_account_id",
                    column: x => x.actor_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_account_history_accounts_target_account_id",
                    column: x => x.target_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "area_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                area_id = table.Column<Guid>(type: "uuid", nullable: true),
                changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                before_data = table.Column<string>(type: "jsonb", nullable: true),
                after_data = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_area_history", x => x.id);
                table.ForeignKey(
                    name: "FK_area_history_accounts_actor_account_id",
                    column: x => x.actor_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_area_history_areas_area_id",
                    column: x => x.area_id,
                    principalTable: "areas",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "auth_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                session_token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auth_sessions", x => x.id);
                table.ForeignKey(
                    name: "FK_auth_sessions_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "department_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                department_id = table.Column<Guid>(type: "uuid", nullable: true),
                before_data = table.Column<string>(type: "jsonb", nullable: true),
                after_data = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_department_history", x => x.id);
                table.ForeignKey(
                    name: "FK_department_history_accounts_actor_account_id",
                    column: x => x.actor_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_department_history_departments_department_id",
                    column: x => x.department_id,
                    principalTable: "departments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "password_reset_requests",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                is_closed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_password_reset_requests", x => x.id);
                table.ForeignKey(
                    name: "FK_password_reset_requests_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "auth_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                action_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                outcome = table.Column<AuditOutcome>(type: "audit_outcome", nullable: false),
                reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: true),
                session_id = table.Column<Guid>(type: "uuid", nullable: true),
                email_submitted = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                blocked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                user_agent = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auth_history", x => x.id);
                table.ForeignKey(
                    name: "FK_auth_history_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_auth_history_accounts_actor_account_id",
                    column: x => x.actor_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_auth_history_auth_sessions_session_id",
                    column: x => x.session_id,
                    principalTable: "auth_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "code", "display_name" },
            values: new object[,]
            {
                { (short)1, "QTTB_MANAGER", "Trưởng phòng QTTB" },
                { (short)2, "QTTB_STAFF", "Nhân viên phòng QTTB" },
                { (short)3, "DEPARTMENT_STAFF", "Nhân viên phòng ban" }
            });

        migrationBuilder.CreateIndex(
            name: "ix_account_history_actor",
            table: "account_history",
            columns: new[] { "actor_account_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_account_history_target",
            table: "account_history",
            columns: new[] { "target_account_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "uq_account_history_request_action",
            table: "account_history",
            columns: new[] { "request_id", "action_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_accounts_account_code",
            table: "accounts",
            column: "account_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_accounts_role_id",
            table: "accounts",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "IX_accounts_user_profile_id",
            table: "accounts",
            column: "user_profile_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_accounts_normalized_email",
            table: "accounts",
            column: "normalized_email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_area_history_actor_account_id",
            table: "area_history",
            column: "actor_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_area_history_target",
            table: "area_history",
            columns: new[] { "area_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "uq_area_history_request_action",
            table: "area_history",
            columns: new[] { "request_id", "action_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_areas_code",
            table: "areas",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_areas_root_normalized_name",
            table: "areas",
            column: "normalized_name",
            unique: true,
            filter: "parent_id IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_areas_sibling_normalized_name",
            table: "areas",
            columns: new[] { "parent_id", "normalized_name" },
            unique: true,
            filter: "parent_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_auth_history_account",
            table: "auth_history",
            columns: new[] { "account_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "IX_auth_history_actor_account_id",
            table: "auth_history",
            column: "actor_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_auth_history_correlation",
            table: "auth_history",
            columns: new[] { "request_id", "action_code", "session_id" });

        migrationBuilder.CreateIndex(
            name: "ix_auth_history_email",
            table: "auth_history",
            columns: new[] { "email_submitted", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_auth_history_session",
            table: "auth_history",
            columns: new[] { "session_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "uq_auth_history_session_ended",
            table: "auth_history",
            column: "session_id",
            unique: true,
            filter: "action_code = 'SESSION_ENDED'");

        migrationBuilder.CreateIndex(
            name: "IX_auth_sessions_session_token_hash",
            table: "auth_sessions",
            column: "session_token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_auth_sessions_active_account",
            table: "auth_sessions",
            column: "account_id",
            unique: true,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "IX_department_history_actor_account_id",
            table: "department_history",
            column: "actor_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_department_history_target",
            table: "department_history",
            columns: new[] { "department_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "uq_department_history_request_action",
            table: "department_history",
            columns: new[] { "request_id", "action_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_departments_code",
            table: "departments",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_departments_normalized_name",
            table: "departments",
            column: "normalized_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_password_reset_requests_token_hash",
            table: "password_reset_requests",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_password_reset_requests_open_account",
            table: "password_reset_requests",
            column: "account_id",
            unique: true,
            filter: "NOT is_closed");

        migrationBuilder.CreateIndex(
            name: "IX_roles_code",
            table: "roles",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_profiles_department_id",
            table: "user_profiles",
            column: "department_id");

        migrationBuilder.Sql("""
            CREATE FUNCTION public.csvc_lock_area_tree() RETURNS trigger
            LANGUAGE plpgsql VOLATILE AS $$
            BEGIN
                -- All tree writers must see a fresh snapshot after acquiring the lock.
                IF current_setting('transaction_isolation') NOT IN ('read committed', 'read uncommitted') THEN
                    RAISE EXCEPTION 'Area tree writes require READ COMMITTED isolation'
                        USING ERRCODE = '25000';
                END IF;
                PERFORM pg_advisory_xact_lock(1129535043, 1);
                RETURN NULL;
            END;
            $$;

            CREATE TRIGGER trg_areas_write_lock
            BEFORE INSERT OR UPDATE OR DELETE ON public.areas
            FOR EACH STATEMENT EXECUTE FUNCTION public.csvc_lock_area_tree();

            CREATE FUNCTION public.csvc_check_area_cycle() RETURNS trigger
            LANGUAGE plpgsql VOLATILE AS $$
            BEGIN
                IF EXISTS (
                    WITH RECURSIVE ancestors AS (
                        SELECT id, parent_id FROM public.areas
                        WHERE id = (SELECT parent_id FROM public.areas WHERE id = NEW.id)
                        UNION
                        SELECT area.id, area.parent_id FROM public.areas area
                        JOIN ancestors ON area.id = ancestors.parent_id
                    )
                    SELECT 1 FROM ancestors WHERE id = NEW.id
                ) THEN
                    RAISE EXCEPTION 'Area hierarchy cannot contain a cycle'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_areas_no_cycle';
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE CONSTRAINT TRIGGER trg_areas_no_cycle
            AFTER INSERT OR UPDATE ON public.areas
            DEFERRABLE INITIALLY IMMEDIATE
            FOR EACH ROW EXECUTE FUNCTION public.csvc_check_area_cycle();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER trg_areas_no_cycle ON public.areas;
            DROP TRIGGER trg_areas_write_lock ON public.areas;
            DROP FUNCTION public.csvc_check_area_cycle();
            DROP FUNCTION public.csvc_lock_area_tree();
            """);
        migrationBuilder.DropTable(
            name: "account_history");

        migrationBuilder.DropTable(
            name: "area_history");

        migrationBuilder.DropTable(
            name: "auth_history");

        migrationBuilder.DropTable(
            name: "auth_login_throttles");

        migrationBuilder.DropTable(
            name: "department_history");

        migrationBuilder.DropTable(
            name: "password_reset_requests");

        migrationBuilder.DropTable(
            name: "areas");

        migrationBuilder.DropTable(
            name: "auth_sessions");

        migrationBuilder.DropTable(
            name: "accounts");

        migrationBuilder.DropTable(
            name: "roles");

        migrationBuilder.DropTable(
            name: "user_profiles");

        migrationBuilder.DropTable(
            name: "departments");

        migrationBuilder.Sql("DROP TYPE public.audit_outcome; DROP TYPE public.entity_status;");
    }
}
