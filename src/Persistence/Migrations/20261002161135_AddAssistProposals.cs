using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261002161135_AddAssistProposals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "assist");

        migrationBuilder.CreateTable(
            name: "action_proposals",
            schema: "assist",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                command = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                before = table.Column<string>(type: "jsonb", nullable: true),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                provenance = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_action_proposals", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_action_proposals_status_proposed_at",
            schema: "assist",
            table: "action_proposals",
            columns: new[] { "status", "proposed_at" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "action_proposals",
            schema: "assist");
    }
}
