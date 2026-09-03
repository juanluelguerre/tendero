using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903074645_AddCartAndCheckout : Migration
{
    /// <summary>
    /// Carts, and the eight columns checkout freezes onto an order.
    ///
    /// EF generated the non-nullable jsonb columns with <c>"{}"</c> as a
    /// default, and those defaults are gone from this file by hand. Two reasons,
    /// and the first is what caught it: a column with a DEFAULT the model does
    /// not declare is schema drift, and `MigrationBaselineTests` compares the
    /// two databases column by column and went red. The second is that the
    /// default was a lie anyway — an existing order carrying <c>{}</c> for its
    /// totals is unreadable, so filling one in would only hide the problem.
    ///
    /// Without a default, adding a NOT NULL column fails loudly on a table that
    /// has rows. There are none: until this migration <c>Order.Place</c> was
    /// called from exactly one place in the repository, a test builder.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "billing_address",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "discounts",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "payment",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "quote",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "shipping",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "shipping_address",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "taxes",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "totals",
            schema: "ordering",
            table: "orders",
            type: "jsonb",
            nullable: false);

        migrationBuilder.CreateTable(
            name: "carts",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                culture = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lines = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_carts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_carts_status_expires_at",
            schema: "ordering",
            table: "carts",
            columns: new[] { "status", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_carts_token",
            schema: "ordering",
            table: "carts",
            column: "token",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "carts",
            schema: "ordering");

        migrationBuilder.DropColumn(
            name: "billing_address",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "discounts",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "payment",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "quote",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "shipping",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "shipping_address",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "taxes",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "totals",
            schema: "ordering",
            table: "orders");
    }
}
