using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExportFacilitySupplierLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_export_slips_facility_id",
                table: "export_slips",
                column: "facility_id");

            migrationBuilder.AddForeignKey(
                name: "FK_export_slips_suppliers_facility_id",
                table: "export_slips",
                column: "facility_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_export_slips_suppliers_facility_id",
                table: "export_slips");

            migrationBuilder.DropIndex(
                name: "IX_export_slips_facility_id",
                table: "export_slips");
        }
    }
}
