using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261007153524_SourceDocumentPictureReading : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "picture_id",
            schema: "knowledge",
            table: "source_documents",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "read_by_model",
            schema: "knowledge",
            table: "source_documents",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "read_with_prompt",
            schema: "knowledge",
            table: "source_documents",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "picture_id",
            schema: "knowledge",
            table: "source_documents");

        migrationBuilder.DropColumn(
            name: "read_by_model",
            schema: "knowledge",
            table: "source_documents");

        migrationBuilder.DropColumn(
            name: "read_with_prompt",
            schema: "knowledge",
            table: "source_documents");
    }
}
