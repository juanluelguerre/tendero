using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <summary>
/// The proposal's version is Postgres's xmin, a system column every table already
/// has: the model maps it (and the snapshot records it), and there is nothing to
/// create. The generated AddColumn would fail on a name the system reserves.
/// </summary>
public partial class _20261002162756_ProposalVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
