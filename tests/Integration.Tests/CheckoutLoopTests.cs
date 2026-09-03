using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Ordering.Features.ManageCart;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.Pricing;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The whole commerce loop, against a real Postgres and the real outbox.
///
/// Search → cart → quote → checkout → stock held → ship → captured → deliver →
/// return → refund → restock. It is the phase's demo written as a test, and it
/// exists because every one of those arrows crosses a context boundary that a
/// unit test mocks away.
///
/// Elasticsearch takes no part: the projection handlers are not registered, and
/// the NDCG gate already covers the index against a real engine.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CheckoutLoopTests(PostgresFixture postgres)
{
    /// <summary>The pans. Two warehouses in the seed, both at zero, which is why
    /// this test stocks what it needs rather than trusting the file.</summary>
    private const string Sku = "B05DEFG606-DEFAULT";

    private static readonly Address ShipTo = Address.Create(
        "Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    /// <summary>
    /// The one that has to work: a shopper fills a basket and ends up with an
    /// order that is paid for, stock that is held, and a cart that is closed.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_shopper_fills_a_basket_and_ends_up_with_a_paid_order()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            A_shopper_fills_a_basket_and_ends_up_with_a_paid_order));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 2, ct);

        Assert.Equal(2, cart.ItemCount);
        Assert.NotEmpty(cart.Token);

        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var chosen = options.Options[0];

        // The quote the shopper is shown, and the fingerprint they agree to.
        var quote = await shop.QuoteAsync(cart.Token, chosen.Amount, ct);

        var placed = await shop.PlaceOrderAsync(cart.Token, chosen.Code, quote.InputHash, ct);

        Assert.Equal(PlaceOrderOutcome.Placed, placed.Outcome);

        var order = placed.Order!;
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal(quote.Totals.Total, order.Total);

        // Everything the quote decided is frozen onto the order, not recomputed.
        Assert.Equal(quote.InputHash, order.Quote.InputHash);
        Assert.Equal(chosen.Amount, order.Shipping.Amount);
        Assert.Equal("Madrid", order.ShippingAddress.City);

        // The cart is closed and its token no longer finds anything.
        Assert.Null(await shop.FindCartAsync(cart.Token, ct));

        // Nothing has touched inventory yet: that is the outbox's job.
        Assert.Equal(0, await shop.HeldAsync(Sku, ct));

        await shop.DrainAsync(ct);

        Assert.Equal(2, await shop.HeldAsync(Sku, ct));

        // Stock held AND payment authorised is what confirms an order, and the
        // two arrive as two separate messages.
        Assert.Equal(OrderStatus.Confirmed, (await shop.OrderAsync(order.Id, ct))!.Status);
    }

    /// <summary>
    /// The refusal. A declined card costs nothing: no order, no reservation, and
    /// a cart the shopper can still pay for with another card.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_declined_card_leaves_no_order_and_an_untouched_cart()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            A_declined_card_leaves_no_order_and_an_untouched_cart));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        var declined = await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, quote.InputHash, ct,
            instrument: FakePaymentProvider.CardDeclined);

        Assert.Equal(PlaceOrderOutcome.PaymentDeclined, declined.Outcome);
        Assert.NotNull(declined.Detail);

        await using var context = shop.Factory.Create();
        Assert.Empty(await context.Orders.ToListAsync(ct));

        // The basket survives, which is the entire point of authorising before
        // placing: the shopper reaches for another card, not for the catalogue.
        var untouched = await shop.FindCartAsync(cart.Token, ct);
        Assert.Equal(1, untouched!.ItemCount);
    }

    /// <summary>
    /// The mismatch path, and the reason a quote carries a fingerprint at all.
    /// The shopper is not wrong and the shop is not wrong; the cart moved
    /// between the two, and the only honest answer is to show the difference.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_cart_that_changed_after_it_was_quoted_is_not_charged()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            A_cart_that_changed_after_it_was_quoted_is_not_charged));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var stale = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        // A second tab, another unit.
        await shop.AddToCartAsync(cart.Token, Sku, 1, ct);

        var refused = await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, stale.InputHash, ct);

        Assert.Equal(PlaceOrderOutcome.PriceChanged, refused.Outcome);

        // The NEW quote comes back, so the screen can say what changed.
        Assert.NotEqual(stale.InputHash, refused.Pricing!.InputHash);
        Assert.True(refused.Pricing.Totals.Total > stale.Totals.Total);

        await using var context = shop.Factory.Create();
        Assert.Empty(await context.Orders.ToListAsync(ct));
    }

    /// <summary>
    /// Retrying is free. With agents this is the normal case rather than the
    /// rare one: same key, same order, and only one hold on the card.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Paying_twice_with_one_key_buys_one_order()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            Paying_twice_with_one_key_buys_one_order));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        var first = await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, quote.InputHash, ct, key: "one-key");

        var second = await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, quote.InputHash, ct, key: "one-key");

        Assert.Equal(PlaceOrderOutcome.Placed, first.Outcome);
        Assert.Equal(PlaceOrderOutcome.AlreadyPlaced, second.Outcome);
        Assert.Equal(first.Order!.Id, second.Order!.Id);

        await using var context = shop.Factory.Create();
        Assert.Single(await context.Orders.ToListAsync(ct));
    }

    /// <summary>
    /// The rest of the loop: shipped takes the money, delivered opens the
    /// window, and a received return puts the goods back on the shelf.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Shipping_captures_the_money_and_a_return_puts_the_goods_back()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            Shipping_captures_the_money_and_a_return_puts_the_goods_back));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 2, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        var order = (await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, quote.InputHash, ct)).Order!;

        await shop.DrainAsync(ct);

        // Ship. Stock is committed and the money is taken — both on shipping,
        // because confirming is a promise and shipping is a fact.
        await shop.MoveAsync(order.Id, moved => moved.Ship(TimeProvider.System), ct);
        await shop.DrainAsync(ct);

        var shipped = (await shop.OrderAsync(order.Id, ct))!;
        Assert.NotNull(shipped.Payment!.CaptureId);
        Assert.Equal(3, await shop.AvailableAsync(Sku, ct));
        Assert.Equal(0, await shop.HeldAsync(Sku, ct));

        // Deliver, which opens the return window.
        await shop.MoveAsync(order.Id, moved => moved.Deliver(TimeProvider.System), ct);
        await shop.DrainAsync(ct);

        var opened = await shop.RequestReturnAsync(order.Id, Sku, 1, ct);
        Assert.Equal(RequestReturnOutcome.Opened, opened.Outcome);

        var returnId = opened.Return!.Id;

        await shop.DecideAsync(returnId, ReturnDecision.Approve, ct);
        await shop.DecideAsync(returnId, ReturnDecision.Receive, ct);

        // Receiving is what restocks, and it travels by the outbox like
        // everything else.
        await shop.DrainAsync(ct);
        Assert.Equal(4, await shop.AvailableAsync(Sku, ct));

        var refunded = await shop.DecideAsync(returnId, ReturnDecision.Refund, ct);

        Assert.Equal(DecideReturnOutcome.Decided, refunded.Outcome);
        Assert.Equal(ReturnStatus.Refunded, refunded.Return!.Status);
        Assert.NotNull(refunded.Return.RefundAmount);
        Assert.True(refunded.Return.RefundAmount!.Value.Amount > 0m);
    }

    // ---------- The shop ----------

    /// <summary>
    /// The application's own container, composed the way the API composes it.
    /// Nothing is swapped out except Elasticsearch, which takes no part.
    /// </summary>
    private sealed class Shop : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public TenderoDbContextFactory Factory { get; }

        private Shop(ServiceProvider provider, TenderoDbContextFactory factory)
        {
            _provider = provider;
            Factory = factory;
        }

        public static async Task<Shop> OpenAsync(PostgresFixture postgres, string name)
        {
            var factory = await postgres.CreateDatabaseAsync(name.ToLowerInvariant());

            await using (var context = factory.Create())
                await context.Database.MigrateAsync();

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
            services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();

            var shop = new Shop(services.BuildServiceProvider(validateScopes: true), factory);

            await shop.ImportAndPublishAsync();

            return shop;
        }

        /// <summary>
        /// Importing never publishes (ADR 0012), and only Active products are
        /// purchasable — so the catalogue has to go through the review queue
        /// before anything can be put in a basket. That is the rule doing its
        /// job rather than an inconvenience.
        /// </summary>
        private async Task ImportAndPublishAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

            await commands.SendAsync(new ImportProductsCommand("seed"));

            await using var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

            foreach (var id in await context.Products.Select(product => product.Id).ToListAsync())
                await commands.SendAsync(new PublishProductCommand(id));
        }

        public async Task StockAsync(string sku, int quantity, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStockLedger>()
                .CountAsync(sku, "MAD", quantity, ct);
        }

        public async Task<CartView> AddToCartAsync(string? token, string sku, int quantity, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();

            var outcome = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new AddToCartCommand(token, sku, quantity, "es"), ct);

            Assert.Equal(CartResult.Changed, outcome.Result);

            return outcome.Cart!;
        }

        public async Task<ShippingOptionsResult> ShippingOptionsAsync(string token, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();

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
        public async Task<CartPricing> QuoteAsync(string token, Money shipping, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();

            var cart = await scope.ServiceProvider.GetRequiredService<ICartRepository>()
                .FindOpenByTokenAsync(token, ct);

            var pricing = await scope.ServiceProvider.GetRequiredService<ICartPricer>()
                .QuoteAsync(cart!.PricingLines(), "es", shipping, [], ct);

            Assert.Equal(PricingOutcome.Quoted, pricing.Outcome);

            return pricing;
        }

        public async Task<PlaceOrderResult> PlaceOrderAsync(
            string token, string option, string hash, CancellationToken ct,
            string instrument = FakePaymentProvider.CardOk, string key = "checkout-1")
        {
            await using var scope = _provider.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new PlaceOrderCommand(
                    token, ShipTo, null, option, hash, instrument, key, [], "es"), ct);
        }

        public async Task<RequestReturnResult> RequestReturnAsync(
            OrderId orderId, string sku, int quantity, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new RequestReturnCommand(
                    orderId, [new ReturnLineRequest(sku, quantity, nameof(ReturnReason.WrongSize), null)], "es"), ct);
        }

        public async Task<DecideReturnResult> DecideAsync(
            ReturnRequestId id, ReturnDecision decision, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new DecideReturnCommand(id, decision, null), ct);
        }

        /// <summary>A transition a screen would drive. It goes through the
        /// aggregate, so the events it raises reach the outbox.</summary>
        public async Task MoveAsync(OrderId id, Action<Order> move, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

            move((await orders.FindByIdAsync(id, ct))!);

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }

        /// <summary>The same loop as <c>OutboxProcessor</c>, without the timer —
        /// and twice, because a handler's own changes raise the next message.</summary>
        public async Task DrainAsync(CancellationToken ct)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                await using var scope = _provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

                var pending = await context.OutboxMessages
                    .Where(message => message.ProcessedAt == null)
                    .OrderBy(message => message.OccurredAt)
                    .ToListAsync(ct);

                if (pending.Count == 0)
                    return;

                foreach (var message in pending)
                {
                    await dispatcher.PublishAsync(
                        DomainEventSerializer.Deserialize(message.Type, message.Payload), ct);
                    message.MarkProcessed();
                }

                await context.SaveChangesAsync(ct);
            }
        }

        public async Task<Cart?> FindCartAsync(string token, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
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
            await using var scope = _provider.CreateAsyncScope();

            return (await scope.ServiceProvider.GetRequiredService<IAvailabilityReader>()
                .AvailableAsync([sku], ct)).GetValueOrDefault(sku, 0);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
