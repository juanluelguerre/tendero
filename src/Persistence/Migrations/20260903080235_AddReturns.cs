using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903080235_AddReturns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "return_requests",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                refund_amount = table.Column<string>(type: "text", nullable: true),
                refund_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lines = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_return_requests", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_return_requests_order_id",
            schema: "ordering",
            table: "return_requests",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "ix_return_requests_status",
            schema: "ordering",
            table: "return_requests",
            column: "status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "return_requests",
            schema: "ordering");
    }
}
