using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902172452_ProductVariants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "variant_axes",
            schema: "catalog",
            table: "products",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.CreateTable(
            name: "variants",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                tax_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                image_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                axis_values = table.Column<string>(type: "jsonb", nullable: false),
                price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_variants", x => x.id);
                table.ForeignKey(
                    name: "fk_variants_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "catalog",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_variants_product_id",
            schema: "catalog",
            table: "variants",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_variants_sku",
            schema: "catalog",
            table: "variants",
            column: "sku",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "variants",
            schema: "catalog");

        migrationBuilder.DropColumn(
            name: "variant_axes",
            schema: "catalog",
            table: "products");
    }
}
