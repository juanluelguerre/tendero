using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902180144_LocalizedAttributes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "attributes",
            schema: "catalog",
            table: "products",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb",
            oldClrType: typeof(string),
            oldType: "jsonb");

        migrationBuilder.CreateTable(
            name: "attribute_definitions",
            schema: "catalog",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                label = table.Column<string>(type: "jsonb", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                is_variant_axis = table.Column<bool>(type: "boolean", nullable: false),
                is_facet = table.Column<bool>(type: "boolean", nullable: false),
                is_searchable = table.Column<bool>(type: "boolean", nullable: false),
                is_draft = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                aliases = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_attribute_definitions", x => x.code);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "attribute_definitions",
            schema: "catalog");

        migrationBuilder.AlterColumn<string>(
            name: "attributes",
            schema: "catalog",
            table: "products",
            type: "jsonb",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "jsonb",
            oldDefaultValueSql: "'[]'::jsonb");
    }
}
