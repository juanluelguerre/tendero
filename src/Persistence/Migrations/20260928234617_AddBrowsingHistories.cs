using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260928234617_AddBrowsingHistories : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "browsing_histories",
            schema: "accounts",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                searches = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                views = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_browsing_histories", x => x.customer_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "browsing_histories",
            schema: "accounts");
    }
}
