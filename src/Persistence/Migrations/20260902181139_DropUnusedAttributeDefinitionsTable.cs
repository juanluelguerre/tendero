using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902181139_DropUnusedAttributeDefinitionsTable : Migration
{
    /// <inheritdoc />
    /// <summary>
    /// The table was created yesterday and nobody reads it: the definitions are
    /// served from the repository's file, which is where they get reviewed in a
    /// diff and translated. A table with no reader is speculative infrastructure,
    /// which is exactly what article 07 argues against keeping — and this one was
    /// mine. It comes back the day the backoffice lets them be edited, together
    /// with the adapter that reads them.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "attribute_definitions",
            schema: "catalog");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attribute_definitions",
            schema: "catalog",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_draft = table.Column<bool>(type: "boolean", nullable: false),
                is_facet = table.Column<bool>(type: "boolean", nullable: false),
                is_searchable = table.Column<bool>(type: "boolean", nullable: false),
                is_variant_axis = table.Column<bool>(type: "boolean", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                label = table.Column<string>(type: "jsonb", nullable: false),
                unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                aliases = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_attribute_definitions", x => x.code);
            });
    }
}
