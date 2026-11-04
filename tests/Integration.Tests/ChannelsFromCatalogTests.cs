using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Features.PublishToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Workers.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Catalog's changes reaching a sales channel (phase 16, S3; ADR 0070), through the
/// outbox and the worker's bridge, against a real Postgres: a product published in
/// Tendero is told to the channel on sale, as the exports say it; archived, the
/// channel is told to archive it; and the listing remembers both.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChannelsFromCatalogTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_product_published_and_then_archived_is_told_to_the_channel_each_time()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("channels_from_catalog");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var channel = new RecordingChannel();
        await using var provider = Compose(factory.ConnectionString, channel);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        // Imported, every product is a Draft: the channel holds it, not on sale.
        Product mug;
        await using (var context = factory.Create())
            mug = await context.Products.Include(product => product.Variants).OrderBy(product => product.Code).FirstAsync(ct);
        Assert.Equal(ChannelProductStatus.NotOnSale, channel.Told.Last(told => told.Code == mug.Code).Status);

        // Five in Madrid while it is a draft: the channel holds the draft, so it is told
        // three — the margin held back — and has it ready for the day it goes on sale.
        // Barcelona, which holds none, is told zero: the channel keeps no number of its own.
        var sku = mug.Variants.First().Sku;
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStockLedger>().ReceiveAsync(sku, "MAD", 5, ct);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }
        Assert.Equal([(sku, "BCN", 0), (sku, "MAD", 3)], channel.Stock.Select(told => (told.Sku, told.Location, told.Available)).Order());

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new PublishProductCommand(mug.Id), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        // Published, its stock is told again only if it differs: it does not, so nothing is sent twice.
        Assert.Equal(2, channel.Stock.Count);

        var onSale = channel.Told.Last(told => told.Code == mug.Code);
        Assert.Equal(ChannelProductStatus.OnSale, onSale.Status);
        Assert.Equal(mug.Name.In("es"), onSale.Name);
        Assert.Equal(mug.Variants.Select(variant => variant.Sku).Order(), onSale.Variants.Select(variant => variant.Sku).Order());

        await using (var scope = provider.CreateAsyncScope())
        {
            var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var tracked = await products.FindByIdAsync(mug.Id, ct);
            tracked!.Archive(new TestClock());
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        // By the id it was published as, so the channel archives that product.
        Assert.Equal(ChannelProduct.Archived(mug.Code) with { ListedAs = "product/" + mug.Code }, channel.Told.Last(told => told.Code == mug.Code));

        await using (var scope = provider.CreateAsyncScope())
        {
            var listing = await scope.ServiceProvider.GetRequiredService<IChannelListings>().FindAsync("recording", mug.Code, ct);
            Assert.Equal((ChannelProductStatus.Archived, "product/" + mug.Code), (listing?.Status, listing?.ExternalProductId));
        }
    }

    private static ServiceProvider Compose(string connectionString, RecordingChannel channel)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json"),
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
                ["Inventory:Warehouses:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "warehouses.sample.json"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(PublishToChannelCommand).Assembly,
            typeof(PublishToChannelsOnProductChange).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddInventory(configuration);
        services.AddChannels(configuration);
        services.AddChannelBridges();
        services.AddKeyedSingleton<ISalesChannel>(channel.Channel, channel);
        services.AddHybridSearch(configuration);
        services.AddSingleton<IProductIndexer>(new NoIndex());
        services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>A channel that remembers what it was told, in order.</summary>
    private sealed class RecordingChannel : ISalesChannel
    {
        public List<ChannelProduct> Told { get; } = [];

        public List<ChannelStock> Stock { get; } = [];

        public string Channel => "recording";

        public Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default)
        {
            Told.Add(product);
            return Task.FromResult(new ChannelListingIds(
                "product/" + product.Code,
                product.Variants.ToDictionary(variant => variant.Sku, variant => "variant/" + variant.Sku, StringComparer.Ordinal)));
        }

        public Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default)
        {
            Stock.Add(stock);
            return Task.FromResult<StockPublication>(new StockPublication.Set());
        }

        public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoIndex : IProductIndexer
    {
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
