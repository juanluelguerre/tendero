using ElGuerre.Tendero.Api;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static ElGuerre.Tendero.Integration.Tests.ShopifyRoundTripTests;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The whole migration against the lab store (phase 16, S2b, P16-16 with P16-21;
/// spec *Measures*): what <c>tools/shopify-seed</c> filled the development store
/// with — the seed's catalogue and stock, and the people and history of
/// <c>tools/shopify-seed/history.json</c> — brought into an empty shop on a real
/// Postgres by the host's one operation, rehearsed first. Everything the rehearsal
/// said would come must come, and what comes is what the history says: every
/// customer, every closed order (the open one said, not brought), its refund, the
/// discount the engine can express proposed and the other left, both gift cards
/// listed, one naming its holder.
///
/// It runs only where the store's credentials are, like the round trips, and its
/// pictures are not fetched. No identity provider is configured, so each customer
/// comes as one the shop can address and not yet sign in, and the import says so
/// once, about the store (the Keycloak road is P16-17's, tested on its own).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LabStoreMigrationTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_lab_store_migrates_as_its_rehearsal_said_and_its_history_expects()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("lab_store_migration");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var proposal = Path.Combine(Path.GetTempPath(), $"promotions-{Guid.NewGuid():N}.json");
        var shopify = secrets!.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        shopify["Shopify:CategoriesFile"] = Seed(Path.Combine("tools", "shopify-seed", "categories.json"));
        shopify["Shopify:LocationsFile"] = Seed(Path.Combine("tools", "shopify-seed", "locations.json"));
        try
        {
            await using var provider = StoreMigrationTests.Compose(factory.ConnectionString, proposal, shopify, store: null);

            var rehearsal = await StoreMigrationTests.RunAsync(provider, rehearse: true, ct);
            await using (var context = factory.Create())
                Assert.Equal(0, await context.Products.CountAsync(ct) + await context.Orders.CountAsync(ct));
            var migration = await StoreMigrationTests.RunAsync(provider, rehearse: false, ct);

            var (said, done) = (StoreMigrationTests.Said(rehearsal), StoreMigrationTests.Said(migration));
            Assert.Empty(migration.NotBrought);
            Assert.All(said.Keys, step => Assert.Equal(said[step], done[step]));

            TestContext.Current.SendDiagnosticMessage(string.Join('\n', done.Select(pair => $"{pair.Key}: {pair.Value}")));

            Assert.Equal((120, 0), (migration.Catalogue!.Created, migration.Catalogue.Failed));
            Assert.True(migration.Stock!.Counted > 0, "no stock was counted");
            Assert.Empty(migration.Stock.Notes);
            Assert.Equal(5, migration.Customers!.Created);
            var unprovisioned = Assert.Single(migration.Customers.Notes);
            Assert.Equal((Accounts.Ports.CustomerNoteCode.IdentityNotProvisioned, null), (unprovisioned.Code, unprovisioned.About));

            // Twelve orders: eleven closed come (one of them cancelled), the open one is said.
            Assert.Equal((11, 1), (migration.Orders!.Created, migration.Orders.Refunds));
            Assert.Equal([OrderNoteCode.StillOpen], migration.Orders.Notes.Select(note => note.Code));
            Assert.Equal(2, migration.Orders.GiftCards.Count);
            Assert.Single(migration.Orders.GiftCards, card => card.Customer is not null);

            Assert.Equal(["PLANTAS15"], migration.Discounts!.Proposed.Select(promotion => promotion.Code));
            Assert.Equal(1, migration.Discounts.Left);

            await using (var context = factory.Create())
            {
                Assert.Equal(120, await context.Products.CountAsync(ct));
                Assert.Equal(11, await context.Orders.CountAsync(ct));
            }
        }
        finally
        {
            File.Delete(proposal);
        }
    }
}
