using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261008064820_PictureDoubts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "picture_doubts",
            schema: "knowledge",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                picture_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                attribute_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                seen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                seen_label = table.Column<string>(type: "jsonb", nullable: false),
                held = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                held_label = table.Column<string>(type: "jsonb", nullable: false),
                confidence = table.Column<double>(type: "double precision", nullable: false),
                read_by_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                read_with_prompt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_picture_doubts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_picture_doubts_product_id_picture_id",
            schema: "knowledge",
            table: "picture_doubts",
            columns: new[] { "product_id", "picture_id" });

        migrationBuilder.CreateIndex(
            name: "ix_picture_doubts_status_raised_at",
            schema: "knowledge",
            table: "picture_doubts",
            columns: new[] { "status", "raised_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "picture_doubts",
            schema: "knowledge");
    }
}
