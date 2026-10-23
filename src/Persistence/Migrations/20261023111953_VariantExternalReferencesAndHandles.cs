using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261023111953_VariantExternalReferencesAndHandles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "barcode",
            schema: "catalog",
            table: "variants",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "handle",
            schema: "catalog",
            table: "product_external_references",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "variant_external_references",
            schema: "catalog",
            columns: table => new
            {
                source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                external_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                variant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_variant_external_references", x => new { x.variant_id, x.source, x.external_id });
                table.ForeignKey(
                    name: "fk_variant_external_references_variants_variant_id",
                    column: x => x.variant_id,
                    principalSchema: "catalog",
                    principalTable: "variants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_variant_external_references_source_external_id",
            schema: "catalog",
            table: "variant_external_references",
            columns: new[] { "source", "external_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "variant_external_references",
            schema: "catalog");

        migrationBuilder.DropColumn(
            name: "barcode",
            schema: "catalog",
            table: "variants");

        migrationBuilder.DropColumn(
            name: "handle",
            schema: "catalog",
            table: "product_external_references");
    }
}
