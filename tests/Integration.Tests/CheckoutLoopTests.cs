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
    /// The basket speaks the language it is read in, and the order the language it
    /// is placed in — not the one the first line happened to be added in.
    ///
    /// Found in a browser on 2026-09-15: the cart line froze one string, so a
    /// basket begun in Spanish kept Spanish names for a shopper browsing in
    /// English, and checkout copied them onto the order along with the cart's
    /// original culture. It needs a real Postgres because the names now travel as
    /// LocalizedText inside the cart's jsonb, and only a round trip proves that.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_basket_filled_in_Spanish_reads_in_English_and_is_ordered_in_English()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            A_basket_filled_in_Spanish_reads_in_English_and_is_ordered_in_English));

        await shop.StockAsync(Sku, 5, ct);

        var filled = await shop.AddToCartAsync(null, Sku, 1, ct);
        var spanish = Assert.Single(filled.Lines).ProductName;

        var english = Assert.Single((await shop.CartAsync(filled.Token, "en", ct)).Lines).ProductName;

        Assert.NotEqual(spanish, english);
        Assert.Equal(spanish, Assert.Single((await shop.CartAsync(filled.Token, "es", ct)).Lines).ProductName);

        var options = await shop.ShippingOptionsAsync(filled.Token, ct);
        var quote = await shop.QuoteAsync(filled.Token, options.Options[0].Amount, ct, culture: "en");

        var placed = await shop.PlaceOrderAsync(
            filled.Token, options.Options[0].Code, quote.InputHash, ct, culture: "en");

        Assert.Equal(PlaceOrderOutcome.Placed, placed.Outcome);
        Assert.Equal("en", placed.Order!.Culture);
        Assert.Equal(english, Assert.Single(placed.Order.Lines).ProductName);
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

    /// <summary>
    /// The loop, counted (the tracking plan, `P9b-6`): what the *Shop pulse*
    /// draws is these four counters, and a counter is a statement no response
    /// carries — so the test listens to them. A declined card, a stale price and
    /// a placed order are three attempts with three outcomes; the cart is counted
    /// once, when it opens; the hold is counted as it is taken and as it becomes a
    /// sale; the return by its reason. Everything here is dispatched outside a
    /// request, so the actor is the system.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_loop_is_counted_as_it_happens()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        using var counted = new InstrumentRecorder(
            "tendero.carts.created", "tendero.checkout.attempts",
            "tendero.inventory.reservations", "tendero.returns.requested");

        await using var shop = await Shop.OpenAsync(postgres, nameof(The_loop_is_counted_as_it_happens));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        await shop.AddToCartAsync(cart.Token, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var stale = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);
        await shop.AddToCartAsync(cart.Token, Sku, 1, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        await shop.PlaceOrderAsync(cart.Token, options.Options[0].Code, stale.InputHash, ct);
        await shop.PlaceOrderAsync(cart.Token, options.Options[0].Code, quote.InputHash, ct,
            instrument: FakePaymentProvider.CardDeclined);
        var order = (await shop.PlaceOrderAsync(cart.Token, options.Options[0].Code, quote.InputHash, ct)).Order!;

        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Ship(TimeProvider.System), ct);
        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Deliver(TimeProvider.System), ct);
        await shop.DrainAsync(ct);
        await shop.RequestReturnAsync(order.Id, Sku, 1, ct);

        // One cart, however many lines went into it.
        Assert.Single(counted.Of("tendero.carts.created"),
            tags => tags["actor.kind"] is "system" && tags["surface"] is "internal");

        Assert.Equal(
            ["payment_declined", "placed", "price_changed"],
            counted.Of("tendero.checkout.attempts").Select(tags => (string)tags["outcome"]!).Order());

        Assert.Contains(counted.Of("tendero.inventory.reservations"), tags => tags["status"] is "held");
        Assert.Contains(counted.Of("tendero.inventory.reservations"), tags => tags["status"] is "committed");

        Assert.Equal("wrong_size", Assert.Single(counted.Of("tendero.returns.requested"))["reason"]);
    }

    /// <summary>
    /// The defect the UI review of 2026-09-11 found through the storefront: one
    /// unit bought, two returns opened for it, both refunded and both restocked —
    /// one refund of money never owed and one phantom unit on the shelf. The
    /// aggregate had the check; the handler handed it no earlier returns to
    /// check against.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task One_unit_bought_is_one_unit_returned_however_many_times_it_is_asked_for()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            One_unit_bought_is_one_unit_returned_however_many_times_it_is_asked_for));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);

        var order = (await shop.PlaceOrderAsync(
            cart.Token, options.Options[0].Code, quote.InputHash, ct)).Order!;

        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Ship(TimeProvider.System), ct);
        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Deliver(TimeProvider.System), ct);
        await shop.DrainAsync(ct);

        Assert.Equal(1, (await shop.GetOrderAsync(order.Id, ct)).Returnable.Single(line => line.Sku == Sku).Quantity);

        Assert.Equal(RequestReturnOutcome.Opened, (await shop.RequestReturnAsync(order.Id, Sku, 1, ct)).Outcome);

        // The page asks the same rule the command enforces, so it stops
        // offering what the command would refuse.
        Assert.Equal(0, (await shop.GetOrderAsync(order.Id, ct)).Returnable.Single(line => line.Sku == Sku).Quantity);

        var second = await shop.RequestReturnAsync(order.Id, Sku, 1, ct);

        Assert.Equal(RequestReturnOutcome.Refused, second.Outcome);
        Assert.Contains("0 left", second.Detail);
    }

    /// <summary>
    /// The race the fix above left open: two requests for the last unit, both
    /// reading the same earlier returns before either saves. Each one alone is
    /// valid, so only the database can refuse the second — both claim return
    /// number 1 of the order, and the unique index on (order, number) lets one
    /// in. Played deterministically with two scopes rather than with timing.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Two_returns_racing_for_the_last_unit_let_only_one_in()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, nameof(
            Two_returns_racing_for_the_last_unit_let_only_one_in));

        await shop.StockAsync(Sku, 5, ct);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var options = await shop.ShippingOptionsAsync(cart.Token, ct);
        var quote = await shop.QuoteAsync(cart.Token, options.Options[0].Amount, ct);
        var order = (await shop.PlaceOrderAsync(cart.Token, options.Options[0].Code, quote.InputHash, ct)).Order!;

        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Ship(TimeProvider.System), ct);
        await shop.DrainAsync(ct);
        await shop.MoveAsync(order.Id, moved => moved.Deliver(TimeProvider.System), ct);
        await shop.DrainAsync(ct);

        // Both read the past — no returns yet — before either writes.
        var first = await shop.PrepareReturnAsync(order.Id, Sku, ct);
        var second = await shop.PrepareReturnAsync(order.Id, Sku, ct);

        await first.SaveAsync(ct);

        await Assert.ThrowsAsync<ConcurrentWriteException>(() => second.SaveAsync(ct));

        await using var context = shop.Factory.Create();
        Assert.Single(await context.ReturnRequests.Where(request => request.OrderId == order.Id).ToListAsync(ct));
    }

    // ---------- The shop ----------

    /// <summary>
    /// The application's own container, composed the way the API composes it.
    /// Nothing is swapped out except Elasticsearch, which takes no part.
    /// </summary>
    private sealed class Shop : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        public TenderoDbContextFactory Factory { get; }

        private Shop(ServiceProvider provider, TenderoDbContextFactory factory)
        {
            this.provider = provider;
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

        public async ValueTask DisposeAsync() => await this.provider.DisposeAsync();
    }
}
