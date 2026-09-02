using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Workers;

/// <summary>
/// Applies the pending migrations at startup, so that
/// <c>dotnet run --project src/AppHost</c> keeps working on a fresh clone.
///
/// This used to be <c>EnsureCreatedAsync</c>, under a note saying migrations
/// would arrive when there was a schema worth preserving. The problem is that
/// <c>EnsureCreated</c> **does nothing when the schema already exists**: it does
/// not compare, does not warn, does not fail. The first additive model change
/// would have left every already-created development database silently wrong, and
/// the symptom would have shown up much later as a column that does not exist.
/// Same class of failure ADR 0012 documents: a promise written with nothing to
/// execute it.
///
/// It is still restricted to Development. In production, applying migrations at
/// startup is a deployment decision and not the process's — and this project has
/// no deployment target to decide it against yet.
/// </summary>
public sealed class SchemaMigrator(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
            return;

        // Naming them as they are applied: when something goes wrong, the first
        // thing anybody wants to know is which migration was running.
        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}", pending.Length, string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
