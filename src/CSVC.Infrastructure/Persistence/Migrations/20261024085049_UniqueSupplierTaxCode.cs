using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueSupplierTaxCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_tax_code",
                table: "suppliers",
                type: "text",
                nullable: true,
                computedColumnSql: "NULLIF(upper(btrim(tax_code)), '')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "uq_suppliers_normalized_tax_code",
                table: "suppliers",
                column: "normalized_tax_code",
                unique: true,
                filter: "normalized_tax_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_suppliers_normalized_tax_code",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "normalized_tax_code",
                table: "suppliers");
        }
    }
}
