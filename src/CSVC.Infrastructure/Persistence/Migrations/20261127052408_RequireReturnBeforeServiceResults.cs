using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireReturnBeforeServiceResults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION require_return_before_service_result() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.execution_status IS DISTINCT FROM OLD.execution_status
                        AND NEW.execution_status IN ('Completed','Failed','NotExecuted')
                        AND EXISTS (SELECT 1 FROM export_device_guards WHERE device_id = NEW.device_id) THEN
                        RAISE EXCEPTION 'Unresolved export custody prevents recording a service result'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_service_result_export_custody';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER service_result_requires_return BEFORE UPDATE OF execution_status ON service_order_items
                    FOR EACH ROW EXECUTE FUNCTION require_return_before_service_result();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER service_result_requires_return ON service_order_items;
                DROP FUNCTION require_return_before_service_result();
                """);
        }
    }
}
