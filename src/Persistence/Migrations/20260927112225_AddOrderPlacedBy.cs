using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260927112225_AddOrderPlacedBy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "placed_by",
            schema: "ordering",
            table: "orders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_orders_placed_by_created_at",
            schema: "ordering",
            table: "orders",
            columns: new[] { "placed_by", "created_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_orders_placed_by_created_at",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "placed_by",
            schema: "ordering",
            table: "orders");
    }
}
