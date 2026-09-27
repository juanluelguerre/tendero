using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260927100623_AddCheckoutSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "contact_email",
            schema: "ordering",
            table: "orders",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "checkout_sessions",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                culture = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                shipping_option_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                shown_quote_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                order_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                coupons = table.Column<string>(type: "jsonb", nullable: false),
                buyer = table.Column<string>(type: "jsonb", nullable: false),
                destination = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_checkout_sessions", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_checkout_sessions_cart_id",
            schema: "ordering",
            table: "checkout_sessions",
            column: "cart_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_checkout_sessions_owner",
            schema: "ordering",
            table: "checkout_sessions",
            column: "owner");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "checkout_sessions",
            schema: "ordering");

        migrationBuilder.DropColumn(
            name: "contact_email",
            schema: "ordering",
            table: "orders");
    }
}
