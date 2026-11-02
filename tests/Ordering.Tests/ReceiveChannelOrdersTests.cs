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

    private static ReceiveChannelOrdersHandler Handler(ChannelOrders channel, Orders orders) =>
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
