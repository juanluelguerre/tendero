using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261030215955_ChannelsState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "channels");

        migrationBuilder.CreateTable(
            name: "cursors",
            schema: "channels",
            columns: table => new
            {
                channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                orders_read_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cursors", x => x.channel);
            });

        migrationBuilder.CreateTable(
            name: "listings",
            schema: "channels",
            columns: table => new
            {
                channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                external_product_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                refusal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                refused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                variant_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_listings", x => new { x.channel, x.code });
            });

        migrationBuilder.CreateTable(
            name: "stock_levels",
            schema: "channels",
            columns: table => new
            {
                channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                expected = table.Column<int>(type: "integer", nullable: true),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                stale_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_levels", x => new { x.channel, x.sku, x.location });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cursors",
            schema: "channels");

        migrationBuilder.DropTable(
            name: "listings",
            schema: "channels");

        migrationBuilder.DropTable(
            name: "stock_levels",
            schema: "channels");
    }
}
