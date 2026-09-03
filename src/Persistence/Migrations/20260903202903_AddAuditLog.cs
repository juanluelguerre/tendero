using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903202903_AddAuditLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.CreateTable(
            name: "audit_entries",
            schema: "audit",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                command_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                customer = table.Column<Guid>(type: "uuid", nullable: true),
                agent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_entries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_agent_at",
            schema: "audit",
            table: "audit_entries",
            columns: new[] { "agent", "at" },
            descending: new[] { false, true },
            filter: "agent IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_at",
            schema: "audit",
            table: "audit_entries",
            column: "at",
            descending: new bool[0]);

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_outcome_at",
            schema: "audit",
            table: "audit_entries",
            columns: new[] { "outcome", "at" },
            descending: new[] { false, true },
            filter: "outcome <> 'Allowed'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_entries",
            schema: "audit");
    }
}
