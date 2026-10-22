using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EditableBusinessCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "account_code_seq");

            migrationBuilder.CreateSequence(
                name: "department_code_seq");

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                table: "departments",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(btrim(code))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                table: "accounts",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(btrim(account_code))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "uq_departments_normalized_code",
                table: "departments",
                column: "normalized_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_accounts_normalized_code",
                table: "accounts",
                column: "normalized_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_departments_normalized_code",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "uq_accounts_normalized_code",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                table: "accounts");

            migrationBuilder.DropSequence(
                name: "account_code_seq");

            migrationBuilder.DropSequence(
                name: "department_code_seq");
        }
    }
}
