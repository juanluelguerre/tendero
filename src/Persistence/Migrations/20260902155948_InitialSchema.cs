using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902155948_InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "outbox");

        migrationBuilder.EnsureSchema(
            name: "ordering");

        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "messages",
            schema: "outbox",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "orders",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                culture = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lines = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "products",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "jsonb", nullable: false),
                slug = table.Column<string>(type: "jsonb", nullable: false),
                description = table.Column<string>(type: "jsonb", nullable: true),
                brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                attributes = table.Column<string>(type: "jsonb", nullable: false),
                price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                images = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_products", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "product_external_references",
            schema: "catalog",
            columns: table => new
            {
                source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                external_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_external_references", x => new { x.product_id, x.source, x.external_id });
                table.ForeignKey(
                    name: "fk_product_external_references_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "catalog",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_messages_processed_at_occurred_at",
            schema: "outbox",
            table: "messages",
            columns: new[] { "processed_at", "occurred_at" },
            filter: "processed_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_orders_idempotency_key",
            schema: "ordering",
            table: "orders",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_external_references_source_external_id",
            schema: "catalog",
            table: "product_external_references",
            columns: new[] { "source", "external_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "messages",
            schema: "outbox");

        migrationBuilder.DropTable(
            name: "orders",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "product_external_references",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "products",
            schema: "catalog");
    }
}
