using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The idempotency keys on a real Postgres, held to the suite the in-memory
/// store passes — where it matters most: of twelve claims at once exactly one
/// is fresh only if the database decides, and a read followed by a write does
/// not.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyStoreTests(PostgresFixture postgres) : IdempotencyStoreContract
{
    protected override void SkipIfUnavailable() => postgres.SkipIfUnavailable();

    protected override async Task<IIdempotencyStore> OpenAsync(TimeProvider clock)
    {
        var factory = await postgres.CreateDatabaseAsync($"idempotency_{Guid.NewGuid():N}"[..24]);
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return new ServiceCollection().AddLogging().AddSingleton(clock).AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IIdempotencyStore>();
    }
}
