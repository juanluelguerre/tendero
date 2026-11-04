using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261104105104_HeldChannelOrders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "held_orders",
            schema: "channels",
            columns: table => new
            {
                channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                about = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                page = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                held_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_tried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_held_orders", x => new { x.channel, x.external_id });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "held_orders",
            schema: "channels");
    }
}
