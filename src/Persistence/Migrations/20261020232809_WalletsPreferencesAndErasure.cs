using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261020232809_WalletsPreferencesAndErasure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "culture_chosen",
            schema: "accounts",
            table: "customers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "erased_at",
            schema: "accounts",
            table: "customers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "marketing_at",
            schema: "accounts",
            table: "customers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "marketing_given",
            schema: "accounts",
            table: "customers",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "marketing_wording",
            schema: "accounts",
            table: "customers",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "wallets",
            schema: "ordering",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                default_method = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                methods = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                to_forget = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallets", x => x.customer_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "wallets",
            schema: "ordering");

        migrationBuilder.DropColumn(
            name: "culture_chosen",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "erased_at",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "marketing_at",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "marketing_given",
            schema: "accounts",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "marketing_wording",
            schema: "accounts",
            table: "customers");
    }
}
