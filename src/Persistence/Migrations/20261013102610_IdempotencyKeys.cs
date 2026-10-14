using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261013102610_IdempotencyKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "idempotency");

        migrationBuilder.CreateTable(
            name: "keys",
            schema: "idempotency",
            columns: table => new
            {
                scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<int>(type: "integer", nullable: true),
                body = table.Column<string>(type: "text", nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_keys", x => new { x.scope, x.key });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "keys",
            schema: "idempotency");
    }
}
