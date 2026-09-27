using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260927091714_AddOrderMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "mode",
            schema: "ordering",
            table: "orders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "Live");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "mode",
            schema: "ordering",
            table: "orders");
    }
}
