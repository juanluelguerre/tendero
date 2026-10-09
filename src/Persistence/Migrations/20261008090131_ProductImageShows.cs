using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261008090131_ProductImageShows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nothing to run: what a picture shows is a key inside the gallery's
        // jsonb, absent for every picture stored so far, and absent reads as
        // "shows nothing". The migration exists for the model snapshot.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {

    }
}
