using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261023191350_SourceCollectionsAndHandleIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "source_collections",
            schema: "catalog",
            columns: table => new
            {
                source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                handle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                category_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_source_collections", x => new { x.source, x.handle });
            });

        migrationBuilder.CreateIndex(
            name: "ix_product_external_references_handle",
            schema: "catalog",
            table: "product_external_references",
            column: "handle");

        migrationBuilder.CreateIndex(
            name: "ix_source_collections_handle",
            schema: "catalog",
            table: "source_collections",
            column: "handle");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "source_collections",
            schema: "catalog");

        migrationBuilder.DropIndex(
            name: "ix_product_external_references_handle",
            schema: "catalog",
            table: "product_external_references");
    }
}
