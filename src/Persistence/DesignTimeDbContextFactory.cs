using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// How <c>dotnet ef</c> builds the context without starting the application.
///
/// The usual alternative — <c>--startup-project src/Api</c> — does not work
/// here: the API demands two connection strings at startup and the AppHost
/// injects them, so the design-time tools would fail with a configuration error
/// that has nothing to do with the schema.
///
/// The string below NEVER connects. Generating and comparing migrations only
/// needs the provider in order to know how to translate the model into
/// PostgreSQL SQL; applying them for real is <c>MigrateAsync</c>'s job, with the
/// real string.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenderoDbContext>
{
    public TenderoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenderoDbContext>()
            .UseNpgsql("Host=localhost;Database=tendero-design-time")
            .Options;

        return new TenderoDbContext(options);
    }
}
