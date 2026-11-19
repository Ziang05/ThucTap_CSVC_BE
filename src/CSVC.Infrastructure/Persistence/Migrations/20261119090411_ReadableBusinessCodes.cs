using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReadableBusinessCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "business_code_seq");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "business_code_seq");
        }
    }
}
