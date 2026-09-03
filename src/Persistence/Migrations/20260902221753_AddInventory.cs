using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902221753_AddInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "inventory");

        migrationBuilder.CreateTable(
            name: "reservations",
            schema: "inventory",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lines = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reservations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "stock_items",
            schema: "inventory",
            columns: table => new
            {
                sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                warehouse_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                on_hand = table.Column<int>(type: "integer", nullable: false),
                reserved = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_items", x => new { x.sku, x.warehouse_code });
            });

        migrationBuilder.CreateIndex(
            name: "ix_reservations_order_id",
            schema: "inventory",
            table: "reservations",
            column: "order_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_items_sku",
            schema: "inventory",
            table: "stock_items",
            column: "sku");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reservations",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "stock_items",
            schema: "inventory");
    }
}
