using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261003111407_AIInvocationSubject : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "subject",
            schema: "telemetry",
            table: "ai_invocations",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_ai_invocations_subject_at",
            schema: "telemetry",
            table: "ai_invocations",
            columns: new[] { "subject", "at" },
            filter: "subject IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_ai_invocations_subject_at",
            schema: "telemetry",
            table: "ai_invocations");

        migrationBuilder.DropColumn(
            name: "subject",
            schema: "telemetry",
            table: "ai_invocations");
    }
}
