using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261024105812_ImportedCustomers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "email",
            schema: "accounts",
            table: "customers",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "imported_external_id",
            schema: "accounts",
            table: "customers",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "imported_source",
            schema: "accounts",
            table: "customers",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_customers_imported_from",
            schema: "accounts",
            table: "customers",
            columns: new[] { "imported_source", "imported_external_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_customers_imported_from",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "email",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "imported_external_id",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "imported_source",
            schema: "accounts",
            table: "customers");
    }
}
