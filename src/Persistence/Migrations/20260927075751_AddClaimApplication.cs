using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260927075751_AddClaimApplication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "application_refused",
            schema: "knowledge",
            table: "claims",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "applied_as",
            schema: "knowledge",
            table: "claims",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "application_refused",
            schema: "knowledge",
            table: "claims");

        migrationBuilder.DropColumn(
            name: "applied_as",
            schema: "knowledge",
            table: "claims");
    }
}
