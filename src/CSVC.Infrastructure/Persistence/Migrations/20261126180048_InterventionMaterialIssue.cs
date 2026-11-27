using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InterventionMaterialIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "issue_document_id",
                table: "equipment_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_changes_issue_document_id",
                table: "equipment_changes",
                column: "issue_document_id");

            migrationBuilder.AddForeignKey(
                name: "FK_equipment_changes_material_documents_issue_document_id",
                table: "equipment_changes",
                column: "issue_document_id",
                principalTable: "material_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_equipment_changes_material_documents_issue_document_id",
                table: "equipment_changes");

            migrationBuilder.DropIndex(
                name: "IX_equipment_changes_issue_document_id",
                table: "equipment_changes");

            migrationBuilder.DropColumn(
                name: "issue_document_id",
                table: "equipment_changes");
        }
    }
}
