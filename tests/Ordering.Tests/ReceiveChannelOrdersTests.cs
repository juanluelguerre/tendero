using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Receiving the orders a sales channel took (phase 16, S3; ADR 0070): each open one
/// becomes an order Tendero ships, once however many times it is read — by the
/// reconciliation's pass or by the one a webhook names — and the pass says how far it
/// read so the next goes on from there.
/// </summary>
public sealed class ReceiveChannelOrdersTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateTimeOffset Since = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    private static SourceOrder Taken(string id, SourceOrderState state = SourceOrderState.Open, string sku = "TAZ-001-BLU", int minutes = 10) =>
        new(id, "#" + id, null, "ana@example.com", "es", Since.AddMinutes(minutes - 1), null, state,
            [new SourceOrderLine($"{id}-1", sku, "Taza de gres", "Azul", Eur(12.5m), 2)],
            Ana, Ana, new SourceShipping("Envío estándar", Eur(4.95m)),
            new SourceOrderTotals(Eur(25m), Eur(0m), Eur(4.95m), Eur(5.20m), Eur(29.95m)),
            [], [new SourceTax("IVA", 0.21m, Eur(5.20m))], [], Since.AddMinutes(minutes));

    private static ReceiveChannelOrdersHandler Handler(IChannelOrderSource channel, Orders orders) =>
        new(new OneChannel(channel), orders, new Catalogue(), orders, Clock);

    [Fact]
    public async Task An_open_order_the_channel_took_becomes_an_order_to_ship_here()
    {
        var orders = new Orders();

        var result = await Handler(new ChannelOrders(Taken("7001")), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        var order = Assert.Single(orders.All);
        Assert.Equal((OrderStatus.PaymentAuthorized, OrderChannel.Shopify), (order.Status, order.Channel));
        Assert.Equal(new OrderOrigin("shopify", "7001", "#7001"), order.ReceivedFrom);
        Assert.Equal(Eur(29.95m), order.Total);
        Assert.Equal([new OrderTax("IVA", 0.21m, Eur(24.76m), Eur(5.20m))], order.Taxes);
        Assert.Equal((1, 0, 0), (result.Received, result.AlreadyHere, result.Skipped));
        Assert.Equal(1, orders.Saves);
    }

    /// <summary>The webhook and the reconciliation both bring it: one order, the second time said to be here.</summary>
    [Fact]
    public async Task An_order_read_twice_is_one_order()
    {
        var orders = new Orders();
        var channel = new ChannelOrders(Taken("7001"));

        await Handler(channel, orders).HandleAsync(new ReceiveChannelOrderCommand("shopify", "7001"), Ct);
        var again = await Handler(channel, orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Single(orders.All);
        Assert.Equal((0, 1), (again.Received, again.AlreadyHere));
    }

    /// <summary>Cancelled or already fulfilled before Tendero saw it: there is nothing here to ship.</summary>
    [Theory]
    [InlineData(SourceOrderState.Cancelled)]
    [InlineData(SourceOrderState.Fulfilled)]
    public async Task An_order_that_is_not_open_is_skipped(SourceOrderState state)
    {
        var orders = new Orders();

        var result = await Handler(new ChannelOrders(Taken("7001", state)), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Empty(orders.All);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task An_order_for_a_product_the_catalogue_does_not_sell_is_skipped_and_said()
    {
        var orders = new Orders();

        var result = await Handler(new ChannelOrders(Taken("7001", sku: "NOT-SOLD")), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Empty(orders.All);
        Assert.Equal(1, result.Skipped);
        Assert.Contains("NOT-SOLD", Assert.Single(result.Notes), StringComparison.Ordinal);
    }

    /// <summary>The next pass goes on from the latest change read; with nothing read it stays where it was.</summary>
    [Fact]
    public async Task The_pass_reads_until_the_latest_change_it_saw()
    {
        var orders = new Orders();

        var read = await Handler(new ChannelOrders(Taken("7001", minutes: 10), Taken("7002", minutes: 25)), orders)
            .HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);
        var nothing = await Handler(new ChannelOrders(), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Equal(Since.AddMinutes(25), read.ReadUntil);
        Assert.Equal(Since, nothing.ReadUntil);
    }

    [Fact]
    public async Task An_order_the_channel_does_not_have_is_nothing_received()
    {
        var orders = new Orders();

        var result = await Handler(new ChannelOrders(), orders).HandleAsync(new ReceiveChannelOrderCommand("shopify", "404"), Ct);

        Assert.Empty(orders.All);
        Assert.Equal((0, 0, 0), (result.Received, result.AlreadyHere, result.Skipped));
    }

    // What follows was found by S3's Stryker close: each test holds a line no other one did.

    /// <summary>Changes older than where the pass began do not move it back.</summary>
    [Fact]
    public async Task The_pass_never_reads_back_before_where_it_began()
    {
        var orders = new Orders();
        var late = Taken("7001") with { UpdatedAt = Since.AddMinutes(-30) };

        var result = await Handler(new LateChannel(late), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Equal(Since, result.ReadUntil);
    }

    /// <summary>A pass that received nothing writes nothing.</summary>
    [Fact]
    public async Task A_pass_that_received_nothing_saves_nothing()
    {
        var orders = new Orders();

        await Handler(new ChannelOrders(Taken("7001", SourceOrderState.Cancelled)), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Equal(0, orders.Saves);
    }

    /// <summary>Every order read is counted by the path it came by and what became of it.</summary>
    [Fact]
    public async Task Each_order_read_is_counted_by_its_path_and_its_outcome()
    {
        using var recorder = new InstrumentRecorder("tendero.channel.orders.received");
        var orders = new Orders();
        var channel = new ChannelOrders(Taken("7001"), Taken("7002", SourceOrderState.Cancelled), Taken("7003", sku: "NOT-SOLD"));

        await Handler(channel, orders).HandleAsync(new ReceiveChannelOrderCommand("shopify", "7001"), Ct);
        await Handler(channel, orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Equal(
            [
                ("shopify", "webhook", "received"),
                ("shopify", "reconciliation", "already_here"),
                ("shopify", "reconciliation", "not_open"),
                ("shopify", "reconciliation", "not_in_catalogue"),
            ],
            recorder.Of("tendero.channel.orders.received")
                .Select(tags => ((string?)tags["channel"], (string?)tags["path"], (string?)tags["outcome"])));
    }

    [Fact]
    public async Task An_order_that_is_not_open_is_said_why()
    {
        var result = await Handler(new ChannelOrders(Taken("7001", SourceOrderState.Cancelled)), new Orders())
            .HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Contains("cancelled", Assert.Single(result.Notes), StringComparison.Ordinal);
    }

    /// <summary>One address is both: an order with only a billing or only a shipping address ships to it; with none it is skipped.</summary>
    [Fact]
    public async Task One_address_serves_as_both_and_none_is_skipped()
    {
        var luis = Address.Create("Luis Gil", "Calle Real 2", null, "Sevilla", null, "41001", "ES");
        var orders = new Orders();
        var channel = new ChannelOrders(
            Taken("7001") with { ShippingAddress = null, BillingAddress = luis },
            Taken("7002") with { ShippingAddress = luis, BillingAddress = null },
            Taken("7003") with { ShippingAddress = null, BillingAddress = null });

        var result = await Handler(channel, orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.All(orders.All, order => Assert.Equal((luis, luis), (order.ShippingAddress, order.BillingAddress)));
        Assert.Equal((2, 1), (result.Received, result.Skipped));
    }

    /// <summary>The order keeps the channel's shipping as a method of its own, and the culture the customer bought in.</summary>
    [Fact]
    public async Task The_order_keeps_the_channels_shipping_and_the_customers_culture()
    {
        var orders = new Orders();

        await Handler(new ChannelOrders(Taken("7001") with { Culture = "en" }), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        var order = Assert.Single(orders.All);
        Assert.Equal(("shopify:shipping", "en"), (order.Shipping.OptionCode, order.Culture));
    }

    /// <summary>What does not make an order is skipped, counted by why, and said: the shopkeeper reads the note.</summary>
    [Fact]
    public async Task What_cannot_be_an_order_is_skipped_and_said_why()
    {
        using var recorder = new InstrumentRecorder("tendero.channel.orders.received");
        var orders = new Orders();
        var wrong = Taken("7002") with { Totals = new SourceOrderTotals(Eur(25m), Eur(0m), Eur(4.95m), Eur(5.20m), Eur(99m)) };
        var nowhere = Taken("7003") with { ShippingAddress = null, BillingAddress = null };

        var shopify = await Handler(new ChannelOrders(wrong, nowhere), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);
        var market = await Handler(new ChannelOrders(Taken("7004")), orders).HandleAsync(new ReceiveChannelOrdersCommand("market", Since), Ct);

        Assert.Empty(orders.All);
        Assert.Collection(
            [.. shopify.Notes, .. market.Notes],
            note => Assert.StartsWith("#7002's figures do not make an order", note, StringComparison.Ordinal),
            note => Assert.Equal("#7003 has nowhere to be sent.", note),
            note => Assert.Equal("market is not a channel an order can come from.", note));
        Assert.Equal(
            ["totals_do_not_add_up", "no_address", "unknown_channel"],
            recorder.Of("tendero.channel.orders.received").Select(tags => (string?)tags["outcome"]));
    }

    /// <summary>
    /// Each skip is said as a value too, not only a sentence (P16-29): the reconciliation
    /// holds the ones that can be mended and retries them, so it needs the order's id and
    /// why, and what it concerns — the SKU the catalogue does not sell.
    /// </summary>
    [Fact]
    public async Task Each_skipped_order_is_said_as_a_value_with_what_it_concerns()
    {
        var wrong = Taken("7002") with { Totals = new SourceOrderTotals(Eur(25m), Eur(0m), Eur(4.95m), Eur(5.20m), Eur(99m)) };
        var nowhere = Taken("7003") with { ShippingAddress = null, BillingAddress = null };
        var channel = new ChannelOrders(Taken("7001", SourceOrderState.Cancelled), wrong, nowhere, Taken("7004", sku: "NOT-SOLD"), Taken("7005"));

        var result = await Handler(channel, new Orders()).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);
        var market = await Handler(new ChannelOrders(Taken("7006")), new Orders()).HandleAsync(new ReceiveChannelOrdersCommand("market", Since), Ct);

        Assert.Equal(
            [
                ("7001", "#7001", ChannelOrderSkip.NotOpen, (string?)null),
                ("7002", "#7002", ChannelOrderSkip.TotalsDoNotAddUp, null),
                ("7003", "#7003", ChannelOrderSkip.NoAddress, null),
                ("7004", "#7004", ChannelOrderSkip.NotInCatalogue, "NOT-SOLD"),
                ("7006", "#7006", ChannelOrderSkip.UnknownChannel, null),
            ],
            result.SkippedOrders.Concat(market.SkippedOrders).Select(skipped => (skipped.ExternalId, skipped.Number, skipped.Why, skipped.About)));
        Assert.Equal(result.Notes, result.SkippedOrders.Select(skipped => skipped.Note));
    }

    /// <summary>A held order read again by its id is counted as held, not as a webhook's.</summary>
    [Fact]
    public async Task A_held_order_read_again_is_counted_by_its_own_path()
    {
        using var recorder = new InstrumentRecorder("tendero.channel.orders.received");

        await Handler(new ChannelOrders(Taken("7001")), new Orders())
            .HandleAsync(new ReceiveChannelOrderCommand("shopify", "7001", ChannelOrderPath.Held), Ct);

        Assert.Equal("held", (string?)Assert.Single(recorder.Of("tendero.channel.orders.received"))["path"]);
    }

    /// <summary>A channel that does not say the customer's culture leaves the shop's.</summary>
    [Fact]
    public async Task Without_a_culture_the_order_is_in_the_shops()
    {
        var orders = new Orders();

        await Handler(new ChannelOrders(Taken("7001") with { Culture = null }), orders).HandleAsync(new ReceiveChannelOrdersCommand("shopify", Since), Ct);

        Assert.Equal("es", Assert.Single(orders.All).Culture);
    }

    /// <summary>A channel whose search returns a change from before the pass began, as a clock skew would.</summary>
    private sealed class LateChannel(SourceOrder order) : IChannelOrderSource
    {
        public string Channel => "shopify";

        public async IAsyncEnumerable<SourceOrder> UpdatedSinceAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return order;
        }

        public Task<SourceOrder?> FindAsync(string externalId, CancellationToken cancellationToken = default) => Task.FromResult<SourceOrder?>(order);
    }

    private sealed class ChannelOrders(params SourceOrder[] orders) : IChannelOrderSource
    {
        public string Channel => "shopify";

        public async IAsyncEnumerable<SourceOrder> UpdatedSinceAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var order in orders.Where(order => order.UpdatedAt >= since))
                yield return order;
        }

        public Task<SourceOrder?> FindAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(orders.FirstOrDefault(order => order.ExternalId == externalId));
    }

    private sealed class OneChannel(IChannelOrderSource source) : IChannelOrderSources
    {
        public IChannelOrderSource Get(string channel) => source;
    }

    private sealed class Orders : IOrderRepository, IUnitOfWork
    {
        public List<Order> All { get; } = [];
        public int Saves { get; private set; }

        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) => Task.FromResult(All.Find(order => order.Id == id));

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.Find(order => order.IdempotencyKey == key));

        public void Add(Order order) => All.Add(order);

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The catalogue sells the blue mug under TAZ-001-BLU.</summary>
    private sealed class Catalogue : ICatalogueVariants
    {
        public Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PurchasableVariant>>(skus
                .Where(sku => sku == "TAZ-001-BLU")
                .ToDictionary(sku => sku, sku => new PurchasableVariant(
                    ProductId.New(), VariantId.New(), sku, LocalizedText.From("es", "Taza de gres"), null, null, "EUR", "KITCHEN", "KITCHEN")));
    }
}
