using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.PublishToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;
using ElGuerre.Tendero.Ordering.Ports;
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
/// The channels' reconciliation against a real Postgres (phase 16, S3; ADR 0070): the
/// first pass marks where a channel starts, the next receives what it took since and
/// moves the mark; telling the catalogue again reaches every live product; and an order
/// the channel took, once shipped here, is reported shipped there.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChannelReconciliationTests(PostgresFixture postgres)
{
    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_pass_receives_what_the_channel_took_since_its_mark_and_the_next_goes_on_from_there()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("channels_reconciliation");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var clock = new TestClock(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var channel = new RecordingChannel();
        var taken = new TakenOrders();
        await using var provider = Compose(factory.ConnectionString, clock, channel, taken);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        string sku;
        await using (var context = factory.Create())
            sku = (await context.Products.Include(product => product.Variants).OrderBy(product => product.Code).FirstAsync(ct)).Variants.First().Sku;

        // The first pass marks the start; an order taken before it is history, not to ship.
        taken.Orders.Add(Taken("7000", sku, clock.GetUtcNow().AddMinutes(-30)));
        await PassAsync(provider, ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        taken.Orders.Add(Taken("7001", sku, clock.GetUtcNow()));
        await PassAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            Assert.Null(await orders.FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "7000"), ct));
            Assert.NotNull(await orders.FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "7001"), ct));

            var cursor = await scope.ServiceProvider.GetRequiredService<IChannelCursors>().FindAsync("shopify", ct);
            Assert.Equal(clock.GetUtcNow(), cursor?.OrdersReadUntil);
        }

        // Told again whole: every live product reaches the channel.
        int live;
        await using (var context = factory.Create())
            live = await context.Products.CountAsync(product => product.Status != Catalog.Domain.ProductStatus.Archived, ct);
        channel.Told.Clear();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ChannelReconciliation>().RepublishAsync(ct);
        Assert.Equal(live, channel.Told.Select(told => told.Code).Distinct().Count());

        // Shipped here, reported there.
        await using (var scope = provider.CreateAsyncScope())
        {
            var order = await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "7001"), ct);
            await scope.ServiceProvider.GetRequiredService<TellTheChannelOnOrderShipped>()
                .HandleAsync(new OrderShipped(order!.Id, clock.GetUtcNow()), ct);
        }
        Assert.Equal("7001", Assert.Single(channel.Shipped).OrderExternalId);
    }

    /// <summary>
    /// Tendero is the system of record for stock: a variant it holds none of is told zero
    /// at every warehouse. Republishing told only the SKUs with a stock row, so on the lab
    /// store's round trip (2026-11-03) three variants the shop has none of kept the
    /// store's own numbers, and the store would have sold them.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Republishing_tells_every_listed_sku_at_every_warehouse_and_none_held_is_zero()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("channels_republish_stock");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var channel = new RecordingChannel();
        await using var provider = Compose(factory.ConnectionString, new TestClock(), channel, new TakenOrders());
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        string sku;
        await using (var context = factory.Create())
            sku = (await context.Products.Include(product => product.Variants).OrderBy(product => product.Code).FirstAsync(ct)).Variants.First().Sku;
        channel.Stock.Clear();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ChannelReconciliation>().RepublishAsync(ct);

        Assert.Equal(
            [("BCN", 0), ("MAD", 0)],
            channel.Stock.Where(stock => stock.Sku == sku).Select(stock => (stock.Location, stock.Available)).Order());
    }

    /// <summary>
    /// An order that cannot be received yet is held, not lost (P16-29): the mark moves past
    /// it, the next passes read it again by its id, and once mended — the product
    /// published — it is received and leaves the list. The lab store's #1013 was this
    /// order, and was received only after rewinding the mark by hand.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_order_that_cannot_be_received_is_held_past_the_mark_and_received_once_mended()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        using var recorder = new InstrumentRecorder("tendero.channel.orders.held", "tendero.channel.orders.released", "tendero.channel.orders.received");
        var (provider, clock, taken, sku) = await StartAsync("channels_held_received", ct);
        await using var _ = provider;

        await PassAsync(provider, ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var unsold = Taken("7002", "NOT-SOLD", clock.GetUtcNow());
        taken.Orders.Add(unsold);
        await PassAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            var held = Assert.Single(await scope.ServiceProvider.GetRequiredService<IHeldChannelOrders>().AllAsync(ct));
            Assert.Equal(("7002", "#7002", HeldOrderReason.NotInCatalogue, "NOT-SOLD", "page/7002", clock.GetUtcNow()),
                (held.ExternalId, held.Number, held.Reason, held.About, held.Page, held.HeldSince));
            Assert.Equal(clock.GetUtcNow(), (await scope.ServiceProvider.GetRequiredService<IChannelCursors>().FindAsync("shopify", ct))?.OrdersReadUntil);
        }

        // Mended on the channel's side without changing the order: the mark alone would never read it again.
        taken.Orders[taken.Orders.IndexOf(unsold)] = unsold with { Lines = [unsold.Lines[0] with { Sku = sku }] };
        clock.Advance(TimeSpan.FromMinutes(5));
        await PassAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "7002"), ct));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IHeldChannelOrders>().AllAsync(ct));
        }

        Assert.Equal(("shopify", "not_in_catalogue"), Tags(Assert.Single(recorder.Of("tendero.channel.orders.held")), "reason"));
        Assert.Equal(("shopify", "received"), Tags(Assert.Single(recorder.Of("tendero.channel.orders.released")), "outcome"));
        Assert.Contains(recorder.Of("tendero.channel.orders.received"), tags => (string?)tags["path"] == "held" && (string?)tags["outcome"] == "received");
    }

    /// <summary>Tried again and still not receivable: it stays, with why it is held now; no order is made.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_held_order_still_not_receivable_stays_with_its_new_reason()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (provider, clock, taken, sku) = await StartAsync("channels_held_still", ct);
        await using var _ = provider;

        await PassAsync(provider, ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var unsold = Taken("7003", "NOT-SOLD", clock.GetUtcNow());
        var heldAt = clock.GetUtcNow();
        taken.Orders.Add(unsold);
        await PassAsync(provider, ct);

        taken.Orders[taken.Orders.IndexOf(unsold)] = unsold with { Lines = [unsold.Lines[0] with { Sku = sku }], ShippingAddress = null, BillingAddress = null };
        clock.Advance(TimeSpan.FromMinutes(5));
        await PassAsync(provider, ct);

        await using var scope = provider.CreateAsyncScope();
        var held = Assert.Single(await scope.ServiceProvider.GetRequiredService<IHeldChannelOrders>().AllAsync(ct));
        Assert.Equal((HeldOrderReason.NoAddress, (string?)null, heldAt, clock.GetUtcNow()), (held.Reason, held.About, held.HeldSince, held.LastTriedAt));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "7003"), ct));
    }

    /// <summary>
    /// Closed on the channel — cancelled and refunded, or gone — a held order has nothing
    /// left to ship and leaves the list. One cancelled before Tendero saw it is never held.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_held_order_closed_on_the_channel_leaves_and_one_never_open_is_never_held()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        using var recorder = new InstrumentRecorder("tendero.channel.orders.released");
        var (provider, clock, taken, _) = await StartAsync("channels_held_closed", ct);
        await using var __ = provider;

        await PassAsync(provider, ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var cancelled = Taken("7004", "NOT-SOLD", clock.GetUtcNow());
        var deleted = Taken("7005", "NOT-SOLD", clock.GetUtcNow());
        taken.Orders.AddRange([cancelled, deleted, Taken("7006", "NOT-SOLD", clock.GetUtcNow()) with { State = SourceOrderState.Cancelled }]);
        await PassAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
            Assert.Equal(["7004", "7005"], (await scope.ServiceProvider.GetRequiredService<IHeldChannelOrders>().AllAsync(ct)).Select(held => held.ExternalId).Order());

        taken.Orders[taken.Orders.IndexOf(cancelled)] = cancelled with { State = SourceOrderState.Cancelled };
        taken.Orders.Remove(deleted);
        clock.Advance(TimeSpan.FromMinutes(5));
        await PassAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IHeldChannelOrders>().AllAsync(ct));
        Assert.Equal(["closed", "closed"], recorder.Of("tendero.channel.orders.released").Select(tags => (string?)tags["outcome"]));
    }

    private static (string?, string?) Tags(IReadOnlyDictionary<string, object?> tags, string second) =>
        ((string?)tags["channel"], (string?)tags[second]);

    /// <summary>A migrated database with the seed imported, and the channel's orders empty.</summary>
    private async Task<(ServiceProvider Provider, TestClock Clock, TakenOrders Taken, string Sku)> StartAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var clock = new TestClock(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        var taken = new TakenOrders();
        var provider = Compose(factory.ConnectionString, clock, new RecordingChannel(), taken);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        await using var context = factory.Create();
        var sku = (await context.Products.Include(product => product.Variants).OrderBy(product => product.Code).FirstAsync(ct)).Variants.First().Sku;
        return (provider, clock, taken, sku);
    }

    private static async Task PassAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ChannelReconciliation>().ReceiveOrdersAsync(ct);
    }

    private static SourceOrder Taken(string id, string sku, DateTimeOffset at) =>
        new(id, "#" + id, null, "ana@example.com", "es", at, null, SourceOrderState.Open,
            [new SourceOrderLine($"{id}-1", sku, "Producto", null, new Money(20m, "EUR"), 1)],
            Ana, Ana, new SourceShipping("Envío", Money.Zero("EUR")),
            new SourceOrderTotals(new Money(20m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"), Money.Zero("EUR"), new Money(20m, "EUR")),
            [], [], [], at);

    private static ServiceProvider Compose(string connectionString, TestClock clock, RecordingChannel channel, TakenOrders taken)
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
        services.AddSingleton<TimeProvider>(clock);
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(PublishToChannelCommand).Assembly,
            typeof(ReceiveChannelOrdersCommand).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddInventory(configuration);
        services.AddOrdering(configuration);
        services.AddChannels(configuration);
        services.AddChannelBridges();
        services.AddScoped<TellTheChannelOnOrderShipped>();
        services.AddKeyedSingleton<ISalesChannel>(channel.Channel, channel);
        services.AddKeyedSingleton<IChannelOrderSource>(channel.Channel, taken);
        services.AddHybridSearch(configuration);
        services.AddSingleton<IProductIndexer>(new NoIndex());
        services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TakenOrders : IChannelOrderSource
    {
        public List<SourceOrder> Orders { get; } = [];

        public string Channel => "shopify";

        public async IAsyncEnumerable<SourceOrder> UpdatedSinceAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var order in Orders.Where(order => order.UpdatedAt >= since).ToList())
                yield return order;
        }

        public Task<SourceOrder?> FindAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.FirstOrDefault(order => order.ExternalId == externalId));
    }

    private sealed class RecordingChannel : ISalesChannel
    {
        public List<ChannelProduct> Told { get; } = [];

        public List<ChannelShipment> Shipped { get; } = [];

        public List<ChannelStock> Stock { get; } = [];

        public string Channel => "shopify";

        public string? OrderPage(string orderExternalId) => "page/" + orderExternalId;

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

        public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default)
        {
            Shipped.Add(shipment);
            return Task.CompletedTask;
        }
    }

    private sealed class NoIndex : IProductIndexer
    {
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task IndexAsync(Catalog.Domain.Product product, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
