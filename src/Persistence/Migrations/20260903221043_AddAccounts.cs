using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903221043_AddAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "accounts");

        migrationBuilder.CreateTable(
            name: "customers",
            schema: "accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                segment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                superseded_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customers", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_customers_subject",
            schema: "accounts",
            table: "customers",
            column: "subject",
            unique: true,
            filter: "subject IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customers",
            schema: "accounts");
    }
}
