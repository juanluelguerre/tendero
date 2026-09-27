using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Ordering.Features.GetOrder;
using ElGuerre.Tendero.Ordering.Features.ManageCart;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The application's own container, composed the way the API composes it.
/// Nothing is swapped out except Elasticsearch, which takes no part. Shared by
/// the checkout loop and UCP's checkout sessions (P11-1): two ways into the same
/// shop, one composition.
/// </summary>
internal sealed class Shop : IAsyncDisposable
{
    /// <summary>Somewhere the flat-rate shipping serves.</summary>
    public static readonly Address ShipTo = Address.Create(
        "Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private readonly ServiceProvider provider;

    public TenderoDbContextFactory Factory { get; }

    private Shop(ServiceProvider provider, TenderoDbContextFactory factory)
    {
        this.provider = provider;
        Factory = factory;
    }

    public static async Task<Shop> OpenAsync(PostgresFixture postgres, string name, IPrincipalAccessor? principal = null)
    {
        var factory = await postgres.CreateDatabaseAsync(name.ToLowerInvariant());

        await using (var context = factory.Create())
            await context.Database.MigrateAsync();

        var shop = Compose(factory, principal);
        await shop.ImportAndPublishAsync();
        return shop;
    }

    /// <summary>A second door into a shop already opened: the same database, somebody else acting.</summary>
    public static Task<Shop> OpenOnAsync(TenderoDbContextFactory factory, IPrincipalAccessor principal) =>
        Task.FromResult(Compose(factory, principal));

    private static Shop Compose(TenderoDbContextFactory factory, IPrincipalAccessor? principal)
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
                ["Inventory:Warehouses:FilePath"] = Seed("warehouses.sample.json")
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(ElGuerre.Tendero.Pricing.Features.QuoteCart.QuoteCartQuery).Assembly,
            typeof(PlaceOrderCommand).Assembly);
        services.AddTenderoPersistence(factory.ConnectionString);
        services.AddCatalog(configuration);
        services.AddPricing(configuration);
        services.AddInventory(configuration);
        services.AddOrdering(configuration);
        services.AddSingleton(principal ?? new SystemPrincipalAccessor());

        return new Shop(services.BuildServiceProvider(validateScopes: true), factory);
    }

    /// <summary>
    /// Importing never publishes (ADR 0012), and only Active products are
    /// purchasable — so the catalogue has to go through the review queue
    /// before anything can be put in a basket. That is the rule doing its
    /// job rather than an inconvenience.
    /// </summary>
    private async Task ImportAndPublishAsync()
    {
        await using var scope = this.provider.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        await commands.SendAsync(new ImportProductsCommand("seed"));

        await using var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        foreach (var id in await context.Products.Select(product => product.Id).ToListAsync())
            await commands.SendAsync(new PublishProductCommand(id));
    }

    public async Task StockAsync(string sku, int quantity, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IStockLedger>()
            .CountAsync(sku, "MAD", quantity, ct);
    }

    public async Task<CartView> AddToCartAsync(string? token, string sku, int quantity, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new AddToCartCommand(token, sku, quantity, "es"), ct);

        Assert.Equal(CartResult.Changed, outcome.Result);

        return outcome.Cart!;
    }

    public async Task<CartView> CartAsync(string token, string culture, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
            .SendAsync(new GetCartQuery(token, culture), ct);
    }

    public async Task<ShippingOptionsResult> ShippingOptionsAsync(string token, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
            .SendAsync(new GetShippingOptionsQuery(token, ShipTo, "es"), ct);

        Assert.Equal(ShippingOutcome.Quoted, result.Outcome);

        return result;
    }

    /// <summary>
    /// The quote the shopper sees, through the same port checkout
    /// revalidates with. Using a different call here would make the
    /// fingerprint comparison prove nothing.
    /// </summary>
    public async Task<CartPricing> QuoteAsync(string token, Money shipping, CancellationToken ct, string culture = "es")
    {
        await using var scope = this.provider.CreateAsyncScope();

        var cart = await scope.ServiceProvider.GetRequiredService<ICartRepository>()
            .FindOpenByTokenAsync(token, ct);

        var pricing = await scope.ServiceProvider.GetRequiredService<ICartPricer>()
            .QuoteAsync(cart!.PricingLines(), culture, shipping, [], ct);

        Assert.Equal(PricingOutcome.Quoted, pricing.Outcome);

        return pricing;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string token, string option, string hash, CancellationToken ct,
        string instrument = FakePaymentProvider.CardOk, string key = "checkout-1", string culture = "es")
    {
        await using var scope = this.provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new PlaceOrderCommand(
                token, ShipTo, null, option, hash, instrument, key, [], culture), ct);
    }

    public async Task<RequestReturnResult> RequestReturnAsync(
        OrderId orderId, string sku, int quantity, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new RequestReturnCommand(
                orderId, [new ReturnLineRequest(sku, quantity, nameof(ReturnReason.WrongSize), null)], "es"), ct);
    }

    /// <summary>A return opened against what its own scope read, and not yet
    /// saved — half of a race, to be finished by the caller.</summary>
    public async Task<PendingReturn> PrepareReturnAsync(OrderId orderId, string sku, CancellationToken ct)
    {
        var scope = this.provider.CreateAsyncScope();
        var order = (await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindByIdAsync(orderId, ct))!;
        var returns = scope.ServiceProvider.GetRequiredService<IReturnRequestRepository>();
        var earlier = await returns.ForOrderAsync(orderId, ct);

        returns.Add(ReturnRequest.Open(
            TimeProvider.System, order,
            [new ReturnLine(order.Lines[0].VariantId, sku, 1, ReturnReason.WrongSize, null)],
            earlier));

        return new PendingReturn(scope);
    }

    public sealed class PendingReturn(AsyncServiceScope scope)
    {
        public async Task SaveAsync(CancellationToken ct)
        {
            await using (scope)
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
    }

    public async Task<OrderDetail> GetOrderAsync(OrderId orderId, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
            .SendAsync(new GetOrderQuery(orderId), ct))!;
    }

    public async Task<DecideReturnResult> DecideAsync(
        ReturnRequestId id, ReturnDecision decision, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new DecideReturnCommand(id, decision, null), ct);
    }

    /// <summary>A transition a screen would drive. It goes through the
    /// aggregate, so the events it raises reach the outbox.</summary>
    public async Task MoveAsync(OrderId id, Action<Order> move, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        move((await orders.FindByIdAsync(id, ct))!);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
    }

    /// <summary>Up to two passes of the real worker (<see cref="WorkerOutbox"/>),
    /// because a handler's own changes raise the next message.</summary>
    /// <summary>Until nothing is left: a saga whose steps each raise the next one's event.</summary>
    public async Task DrainAllAsync(CancellationToken ct)
    {
        for (var pass = 0; pass < 10; pass++)
        {
            if (await WorkerOutbox.PassAsync(this.provider, ct) == 0)
                return;
        }

        Assert.Fail("The outbox was still busy after ten passes.");
    }

    public async Task DrainAsync(CancellationToken ct)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            if (await WorkerOutbox.PassAsync(this.provider, ct) == 0)
                return;
        }
    }

    public async Task<Cart?> FindCartAsync(string token, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICartRepository>()
            .FindOpenByTokenAsync(token, ct);
    }

    public async Task<Order?> OrderAsync(OrderId id, CancellationToken ct)
    {
        await using var context = Factory.Create();
        return await context.Orders.AsNoTracking().FirstOrDefaultAsync(order => order.Id == id, ct);
    }

    public async Task<int> HeldAsync(string sku, CancellationToken ct)
    {
        await using var context = Factory.Create();
        return await context.StockItems.AsNoTracking()
            .Where(item => item.Sku == sku)
            .SumAsync(item => item.Reserved, ct);
    }

    public async Task<int> AvailableAsync(string sku, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IAvailabilityReader>()
            .AvailableAsync([sku], ct)).GetValueOrDefault(sku, 0);
    }

    /// <summary>Any command, dispatched through the pipeline the API uses — validation and audit included.</summary>
    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, ct);
    }

    public async Task<TResult> AskAsync<TResult>(IQuery<TResult> query, CancellationToken ct)
    {
        await using var scope = this.provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().SendAsync(query, ct);
    }

    public async ValueTask DisposeAsync() => await this.provider.DisposeAsync();
}
