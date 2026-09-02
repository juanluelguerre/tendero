using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260902181139_DropUnusedAttributeDefinitionsTable : Migration
{
    /// <inheritdoc />
    /// <summary>
    /// La tabla se creó ayer y nadie la lee: las definiciones se sirven del
    /// fichero del repositorio, que es donde se revisan en un diff y se
    /// traducen. Una tabla sin lector es infraestructura especulativa, que es
    /// justo lo que el artículo 07 argumenta que no hay que guardar — y esta la
    /// puse yo. Vuelve el día que el backoffice permita editarlas, junto con el
    /// adaptador que las lea.
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
