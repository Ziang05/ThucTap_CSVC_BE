using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExportSlipsWithoutApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("ck_export_slips_approval", "export_slips");
            migrationBuilder.DropCheckConstraint("ck_export_slips_status", "export_slips");
            migrationBuilder.Sql("""
                UPDATE export_slips SET status = 'InProgress', version = version + 1
                WHERE status = 'PendingApproval';
                SET CONSTRAINTS ALL IMMEDIATE;
                """);
            migrationBuilder.AddCheckConstraint("ck_export_slips_status", "export_slips",
                "status IN ('InProgress','Completed','Canceled')");
            migrationBuilder.Sql(ExportRecordingIntegritySql.Install);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("ck_export_slips_status", "export_slips");
            migrationBuilder.Sql(ExportRecordingIntegritySql.Remove);
            migrationBuilder.AddCheckConstraint("ck_export_slips_status", "export_slips",
                "status IN ('PendingApproval','InProgress','Completed','Canceled')");
            migrationBuilder.AddCheckConstraint("ck_export_slips_approval", "export_slips",
                "status NOT IN ('InProgress','Completed') OR (approved_by_account_id IS NOT NULL AND approved_at IS NOT NULL AND approval_request_id IS NOT NULL)");
        }
    }
}
