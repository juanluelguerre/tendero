using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260928224504_AddPricingCampaigns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "pricing");

        migrationBuilder.CreateTable(
            name: "campaigns",
            schema: "pricing",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "jsonb", nullable: false),
                line = table.Column<string>(type: "jsonb", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                category_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                image_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                image_alt = table.Column<string>(type: "jsonb", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                promotion_codes = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_campaigns", x => x.code);
            });

        migrationBuilder.CreateIndex(
            name: "ix_campaigns_status_ends_at",
            schema: "pricing",
            table: "campaigns",
            columns: new[] { "status", "ends_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "campaigns",
            schema: "pricing");
    }
}
