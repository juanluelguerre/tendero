using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261002171549_AddAIInvocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "telemetry");

        migrationBuilder.CreateTable(
            name: "ai_invocations",
            schema: "telemetry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                profile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                conversation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                input_tokens = table.Column<long>(type: "bigint", nullable: false),
                output_tokens = table.Column<long>(type: "bigint", nullable: false),
                cost_micro_euros = table.Column<long>(type: "bigint", nullable: false),
                latency_ms = table.Column<int>(type: "integer", nullable: false),
                outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_invocations", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ai_invocations_at",
            schema: "telemetry",
            table: "ai_invocations",
            column: "at",
            descending: new bool[0]);

        migrationBuilder.CreateIndex(
            name: "ix_ai_invocations_conversation_id",
            schema: "telemetry",
            table: "ai_invocations",
            column: "conversation_id",
            filter: "conversation_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ai_invocations",
            schema: "telemetry");
    }
}
