using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentAIService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenComplianceAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_documents_EntityId_DocumentType_FileHash",
                table: "documents");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "document_validations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_validation_events_DocumentValidationRecordId",
                table: "validation_events",
                column: "DocumentValidationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_EntityId_DocumentType_FileHash",
                table: "documents",
                columns: new[] { "EntityId", "DocumentType", "FileHash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_validation_events_document_validations_DocumentValidationRe~",
                table: "validation_events",
                column: "DocumentValidationRecordId",
                principalTable: "document_validations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_validation_events_entities_EntityId",
                table: "validation_events",
                column: "EntityId",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_validation_events_document_validations_DocumentValidationRe~",
                table: "validation_events");

            migrationBuilder.DropForeignKey(
                name: "FK_validation_events_entities_EntityId",
                table: "validation_events");

            migrationBuilder.DropIndex(
                name: "IX_validation_events_DocumentValidationRecordId",
                table: "validation_events");

            migrationBuilder.DropIndex(
                name: "IX_documents_EntityId_DocumentType_FileHash",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "document_validations");

            migrationBuilder.CreateIndex(
                name: "IX_documents_EntityId_DocumentType_FileHash",
                table: "documents",
                columns: new[] { "EntityId", "DocumentType", "FileHash" });
        }
    }
}
