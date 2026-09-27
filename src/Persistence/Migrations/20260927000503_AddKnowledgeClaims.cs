using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260927000503_AddKnowledgeClaims : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "knowledge");

        migrationBuilder.CreateTable(
            name: "claims",
            schema: "knowledge",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                statement = table.Column<string>(type: "jsonb", nullable: false),
                attribute_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reviewed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                evidence = table.Column<string>(type: "jsonb", nullable: false),
                provenance = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_claims", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "source_documents",
            schema: "knowledge",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                text = table.Column<string>(type: "text", nullable: false),
                retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                failed_attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_source_documents", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_claims_product_id_status",
            schema: "knowledge",
            table: "claims",
            columns: new[] { "product_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_source_documents_product_id_culture",
            schema: "knowledge",
            table: "source_documents",
            columns: new[] { "product_id", "culture" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "claims",
            schema: "knowledge");

        migrationBuilder.DropTable(
            name: "source_documents",
            schema: "knowledge");
    }
}
