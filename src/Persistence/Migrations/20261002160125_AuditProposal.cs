using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261002160125_AuditProposal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "proposal",
            schema: "audit",
            table: "audit_entries",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_proposal",
            schema: "audit",
            table: "audit_entries",
            column: "proposal",
            filter: "proposal IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_audit_entries_proposal",
            schema: "audit",
            table: "audit_entries");

        migrationBuilder.DropColumn(
            name: "proposal",
            schema: "audit",
            table: "audit_entries");
    }
}
