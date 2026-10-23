using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261023201624_ImportRunsAndFirstPublication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "first_published_at",
            schema: "catalog",
            table: "products",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "import_runs",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                products = table.Column<Guid[]>(type: "uuid[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_import_runs", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "import_runs",
            schema: "catalog");

        migrationBuilder.DropColumn(
            name: "first_published_at",
            schema: "catalog",
            table: "products");
    }
}
