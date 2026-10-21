using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261020212557_AddressBooks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "address_books",
            schema: "accounts",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_default = table.Column<Guid>(type: "uuid", nullable: true),
                billing_default = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                entries = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_address_books", x => x.customer_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "address_books",
            schema: "accounts");
    }
}
