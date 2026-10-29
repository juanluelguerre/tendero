using ElGuerre.Tendero.Accounts;
using ElGuerre.Tendero.Accounts.Features.LinkIdentity;
using ElGuerre.Tendero.Api;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Features.ImportStock;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Features.ImportOrders;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing;
using ElGuerre.Tendero.Pricing.Features.ProposePromotions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Shopify;
using ElGuerre.Tendero.Shopify.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A store's whole migration, rehearsed and then run (phase 16, S2b, P16-21; spec
/// *Measures*: "everything the rehearsal said would come did, and nothing else").
/// An empty shop, a Shopify store from the adapters' recorded answers, the five
/// steps through the host's one operation, and a real Postgres. The rehearsal
/// writes nothing, and still judges the stock, the orders and the discounts against
/// the catalogue and the customers its earlier steps would bring.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StoreMigrationTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Everything_the_rehearsal_said_would_come_did_and_nothing_else()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("store_migration");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var proposal = Path.Combine(Path.GetTempPath(), $"promotions-{Guid.NewGuid():N}.json");
        var locations = Path.Combine(Path.GetTempPath(), $"locations-{Guid.NewGuid():N}.json");
        // A file, as the merchant writes it: a store's location id holds the configuration's separator.
        await File.WriteAllTextAsync(locations, """{"gid://shopify/Location/71":"MAD","gid://shopify/Location/72":"BCN"}""", ct);
        var categories = Path.Combine(Path.GetTempPath(), $"categories-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(categories, """{"gid://shopify/TaxonomyCategory/hg-11-1-3":"COFFEE_MAKER"}""", ct);
        try
        {
            await using var provider = Compose(factory.ConnectionString, proposal, locations, categories);

            var rehearsal = await RunAsync(provider, rehearse: true, ct);
            await using (var context = factory.Create())
            {
                Assert.Equal(0, await context.Products.CountAsync(ct));
                Assert.Equal(0, await context.Orders.CountAsync(ct));
            }
            Assert.False(File.Exists(proposal));

            var migration = await RunAsync(provider, rehearse: false, ct);

            Assert.Empty(rehearsal.NotBrought);
            var (said, done) = (Said(rehearsal), Said(migration));
            Assert.All(said.Keys, step => Assert.Equal(said[step], done[step]));

            // What the rehearsal leans on its earlier steps for, it was right to: the
            // orders found their lines and their buyers, and the migration stored them.
            Assert.True(rehearsal.Stock!.Counted > 0, "the rehearsal counted no stock");
            Assert.True(rehearsal.Orders!.Created > 0, "the rehearsal brought no order");
            Assert.DoesNotContain(rehearsal.Orders.Notes, note => note.Code == Ordering.Ports.OrderNoteCode.ProductNotInCatalogue);
            await using (var context = factory.Create())
            {
                Assert.Equal(rehearsal.Catalogue!.Created, await context.Products.CountAsync(ct));
                Assert.Equal(rehearsal.Orders.Created, await context.Orders.CountAsync(ct));
            }
        }
        finally
        {
            File.Delete(proposal);
            File.Delete(locations);
            File.Delete(categories);
        }
    }

    /// <summary>
    /// A source with an adapter for some steps only (spec, *The degraded path*): the
    /// others are said as not brought, and the rest run.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_steps_a_source_has_no_adapter_for_are_said_and_the_rest_run()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("store_migration_degraded");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);
        await using var provider = Compose(factory.ConnectionString, "unused.json", "unused.json", "unused.json");
        await using var scope = provider.CreateAsyncScope();

        var rehearsal = await scope.ServiceProvider.GetRequiredService<StoreMigration>().RunAsync("seed", "en", rehearse: true, ct);

        Assert.Equal([MigrationStep.Stock, MigrationStep.Customers, MigrationStep.Orders, MigrationStep.Discounts], rehearsal.NotBrought);
        Assert.True(rehearsal.Catalogue!.Created > 0);
        Assert.Null(rehearsal.Orders);
    }

    /// <summary>
    /// A collection a rehearsal would keep is where a discount rehearsed in the same
    /// request finds its category, ahead of what is kept; outside that request it is
    /// not there. The fixtures' one collection holds a product with no category, so
    /// the migration above never keeps one: this says it directly.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_collection_a_rehearsal_would_keep_is_read_in_its_request_and_nowhere_else()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("store_migration_collections");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);
        await using var provider = Compose(factory.ConnectionString, "unused.json", "unused.json", "unused.json");

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IRehearsedCatalogue>().Keep("cocina", "KITCHEN");
            Assert.Equal("KITCHEN", await scope.ServiceProvider.GetRequiredService<ISourceAddresses>().CategoryAsync("cocina", ct));
        }

        await using (var scope = provider.CreateAsyncScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ISourceAddresses>().CategoryAsync("cocina", ct));
    }

    /// <summary>
    /// What a run said, step by step, without what only a run for real can say: its
    /// run's id, the time it took, the file it wrote, and the ids it minted — a
    /// rehearsed customer has an id of its own, so a gift card says whether it names
    /// one, not which.
    /// </summary>
    internal static Dictionary<MigrationStep, string> Said(StoreMigrationResult result) => new()
    {
        [MigrationStep.Catalogue] = $"{result.Catalogue!.Created} {result.Catalogue.Updated} {result.Catalogue.Failed} {string.Join("|", result.Catalogue.Notes)}",
        [MigrationStep.Stock] = $"{result.Stock!.Counted} {result.Stock.Skipped} {string.Join("|", result.Stock.Notes)}",
        [MigrationStep.Customers] = $"{result.Customers!.Created} {result.Customers.AlreadyImported} {result.Customers.Linked} {string.Join("|", result.Customers.Notes)}",
        [MigrationStep.Orders] = $"{result.Orders!.Created} {result.Orders.AlreadyImported} {result.Orders.Refunds} {string.Join("|", result.Orders.Notes)} "
            + string.Join("|", result.Orders.GiftCards.Select(card => (card.LastCharacters, card.Balance, card.ExpiresOn, card.Customer is not null, card.CustomerAtSource))),
        [MigrationStep.Discounts] = $"{string.Join("|", result.Discounts!.Proposed.Select(promotion => promotion.Code))} {result.Discounts.Left} {string.Join("|", result.Discounts.Notes)}",
    };

    internal static async Task<StoreMigrationResult> RunAsync(ServiceProvider provider, bool rehearse, CancellationToken ct)
    {
        // One scope, as one request is: what the rehearsal's earlier steps would bring lives in it.
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StoreMigration>().RunAsync("shopify", "en", rehearse, ct);
    }

    private static ServiceProvider Compose(string connectionString, string proposal, string locations, string categories) =>
        Compose(
            connectionString,
            proposal,
            new()
            {
                ["Shopify:Store"] = FakeShopify.Store,
                ["Shopify:ClientId"] = "client-id",
                ["Shopify:ClientSecret"] = "client-secret",
                ["Shopify:LocationsFile"] = locations,
                ["Shopify:CategoriesFile"] = categories,
                ["Shopify:BulkPollInterval"] = "00:00:00",
                ["Shopify:ThrottleBackoff"] = "00:00:00",
            },
            new FakeShopify());

    /// <summary>
    /// The host's migration over the seed's files and a real database, reaching the
    /// store through <paramref name="shopify"/>'s settings: its recorded answers when
    /// <paramref name="store"/> is given, the store itself when it is null.
    /// </summary>
    internal static ServiceProvider Compose(
        string connectionString, string proposal, Dictionary<string, string?> shopify, HttpMessageHandler? store)
    {
        static string Seed(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Seed("products.sample.json"),
                ["Catalog:Attributes:FilePath"] = Seed("attributes.sample.json"),
                ["Catalog:Categories:FilePath"] = Seed("categories.sample.json"),
                ["Pricing:Seed:PriceListsPath"] = Seed("pricelists.sample.json"),
                ["Pricing:Seed:PromotionsPath"] = Seed("promotions.sample.json"),
                ["Pricing:Seed:ProposedPromotionsPath"] = proposal,
                ["Inventory:Warehouses:FilePath"] = Seed("warehouses.sample.json"),
            })
            .AddInMemoryCollection(shopify)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(ProposePromotionsCommand).Assembly,
            typeof(ImportStockCommand).Assembly,
            typeof(ImportOrdersCommand).Assembly,
            typeof(LinkIdentityCommand).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddPricing(configuration);
        services.AddInventory(configuration);
        services.AddOrdering(configuration);
        services.AddAccounts();
        services.AddShopify(configuration);
        services.AddScoped<StoreMigration>();
        services.AddSingleton<IPrincipalAccessor>(new SystemPrincipalAccessor());

        // The store is its recorded answers when there are some; its pictures are never fetched.
        if (store is not null)
            services.AddHttpClient("shopify").ConfigurePrimaryHttpMessageHandler(() => store).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services.AddSingleton<IExternalImageReader, NoPictures>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class NoPictures : IExternalImageReader
    {
        public Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default) => Task.FromResult<StoredImage?>(null);
    }
}
