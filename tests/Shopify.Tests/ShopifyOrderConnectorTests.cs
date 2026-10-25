using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Ordering.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>The Shopify adapter of the orders port passes the contract every order source does.</summary>
public sealed class ShopifyOrderConnectorContractTests : OrderSourceConnectorContractTests
{
    protected override IOrderSourceConnector CreateConnector() => ShopifyOrderConnectorTests.AConnector().Connector;
}

/// <summary>
/// A store's orders in the shop's words (phase 16, S2b, P16-18): each line by the
/// SKU the catalogue sells it under — asked of the catalogue, as the stock's are —
/// with the names the buyer saw; the figures as the store charged them, made into
/// an order's (the store's subtotal is after its discounts, ours before); where it
/// stands; and each refund with the lines it took back. Read in pages, because a
/// refund's lines are a connection inside a list.
/// </summary>
public sealed class ShopifyOrderConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    internal static (ShopifyOrderConnector Connector, FakeShopify Store) AConnector()
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);
        return (new ShopifyOrderConnector(admin, new CatalogueSkus()), store);
    }

    private static async Task<List<SourceOrder>> Orders()
    {
        var orders = new List<SourceOrder>();
        await foreach (var order in AConnector().Connector.StreamOrdersAsync(Ct))
            orders.Add(order);
        return orders;
    }

    [Fact]
    public async Task A_fulfilled_order_comes_with_its_lines_by_our_skus_and_its_figures_as_an_orders()
    {
        var moka = (await Orders())[0];

        Assert.Equal(("5001", "#1001", "6001", "ana@example.com", "es"), (moka.ExternalId, moka.Number, moka.CustomerExternalId, moka.ContactEmail, moka.Culture));
        Assert.Equal((SourceOrderState.Fulfilled, new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero)), (moka.State, moka.ShippedAt));
        Assert.Equal(
            [new SourceOrderLine("11", "MOKA-OLV", "Cafetera Moka", "Oliva", Eur(29.90m), 1), new SourceOrderLine("12", "8002-44003", "Taza de gres", null, Eur(10m), 1)],
            moka.Lines);
        Assert.Equal(new SourceOrderTotals(Eur(39.90m), Eur(5m), Eur(4.95m), Eur(6.92m), Eur(39.85m)), moka.Totals);
        Assert.Equal([new SourceDiscount("KITCHEN5", "KITCHEN5", Eur(5m))], moka.Discounts);
        Assert.Equal([new SourceTax("IVA", 0.21m, Eur(6.92m))], moka.Taxes);
        Assert.Equal(new SourceShipping("Envío estándar", Eur(4.95m)), moka.Shipping);
        Assert.Equal("Madrid", moka.ShippingAddress!.City);
    }

    [Fact]
    public async Task A_refund_comes_with_the_lines_it_took_back()
    {
        var refund = Assert.Single((await Orders())[0].Refunds);

        Assert.Equal(("7001", new DateTimeOffset(2026, 4, 20, 16, 0, 0, TimeSpan.Zero), Eur(10m)), (refund.ExternalId, refund.At, refund.Amount));
        Assert.Equal([("12", 1)], refund.Lines);
    }

    /// <summary>
    /// An order is placed, and a refund made, on the day the store says it was
    /// processed, not the day it was written into the store: the two are the same
    /// for an order taken there, and apart for one the store itself imported from
    /// elsewhere — every order in the lab store, written by <c>orderCreate</c>
    /// (P16-16), was created on the day of the seeding.
    /// </summary>
    [Fact]
    public async Task An_order_is_placed_on_the_day_the_store_processed_it()
    {
        var moka = (await Orders())[0];

        Assert.Equal(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero), moka.PlacedAt);
        Assert.Equal(new DateTimeOffset(2026, 4, 20, 16, 0, 0, TimeSpan.Zero), moka.Refunds[0].At);
    }

    /// <summary>
    /// Cancelled when the store says so, open while not all of it left (a partial
    /// fulfilment ships nothing yet); every page is read, the second after the
    /// first's cursor.
    /// </summary>
    [Fact]
    public async Task Where_an_order_stands_is_where_the_store_says()
    {
        var orders = await Orders();

        Assert.Equal([SourceOrderState.Fulfilled, SourceOrderState.Cancelled, SourceOrderState.Open], orders.Select(order => order.State));
        Assert.Equal([null, null], orders.Skip(1).Select(order => order.ShippedAt));
    }

    /// <summary>A guest's order has no buyer id, no shipping line costs nothing, and only an address that can be posted to comes.</summary>
    [Fact]
    public async Task A_guests_order_comes_with_what_it_has()
    {
        var guest = (await Orders())[1];

        Assert.Null(guest.CustomerExternalId);
        Assert.Null(guest.ShippingAddress);
        Assert.Equal(("en", "Sevilla"), (guest.Culture, guest.BillingAddress!.City));
        Assert.Equal(new SourceShipping("", Eur(0m)), guest.Shipping);
        Assert.Equal(new SourceOrderTotals(Eur(10m), Eur(0m), Eur(0m), Eur(1.74m), Eur(10m)), guest.Totals);
    }

    /// <summary>A line whose variant the store deleted has no SKU, and a language the shop does not speak is none.</summary>
    [Fact]
    public async Task What_the_shop_cannot_name_is_left_unnamed()
    {
        var open = (await Orders())[2];

        Assert.Equal(["MOKA-OLV", null], open.Lines.Select(line => line.Sku));
        Assert.Null(open.Culture);
        Assert.Empty(open.Discounts);
    }

    // The tests below, and orders-figures.json, were found by phase 16 S2b's Stryker
    // close: each mutant they name survived the suite above.

    /// <summary>The first page is asked with no cursor, the second after the first's.</summary>
    [Fact]
    public async Task Every_page_is_read_the_next_after_the_cursor()
    {
        var (connector, store) = AConnector();

        await foreach (var _ in connector.StreamOrdersAsync(Ct))
        {
        }

        var pages = store.Queries.Where(query => query.Contains("{ orders(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.Contains("{ orders(first: 50, sortKey: PROCESSED_AT) {", pages[0], StringComparison.Ordinal);
        Assert.Contains("after: \"c2\")", pages[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The subtotal is each line's price times its quantity; a discount is what its
    /// allocations took off the lines and the shipping, added up, named by its code or
    /// by its title when it had none; an order shipped twice left on the last day.
    /// </summary>
    [Fact]
    public async Task An_orders_figures_add_up_as_the_store_allocated_them()
    {
        var (connector, store) = AConnector();
        store.OrdersFixture = "orders-figures.json";

        var orders = new List<SourceOrder>();
        await foreach (var order in connector.StreamOrdersAsync(Ct))
            orders.Add(order);

        var mugs = orders[0];
        Assert.Equal(Eur(40m), mugs.Totals.Subtotal);
        Assert.Equal([new SourceDiscount("TAZAS5", "TAZAS5", Eur(5m)), new SourceDiscount("Envío gratis", "Envío gratis", Eur(5m))], mugs.Discounts);
        Assert.Equal(new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero), mugs.ShippedAt);
        Assert.Equal(Address.Create("Ana Ruiz", "Calle Mayor 1", "2º B", "Madrid", "M", "28013", "ES", "+34600000001"), mugs.ShippingAddress);
    }

    /// <summary>A fulfilled order the store kept no fulfilment for left, as far as anybody knows, the day it was placed.</summary>
    [Fact]
    public async Task A_fulfilled_order_with_no_fulfilment_left_the_day_it_was_placed()
    {
        var (connector, store) = AConnector();
        store.OrdersFixture = "orders-figures.json";

        var orders = new List<SourceOrder>();
        await foreach (var order in connector.StreamOrdersAsync(Ct))
            orders.Add(order);

        Assert.Equal((SourceOrderState.Fulfilled, new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)), (orders[1].State, orders[1].ShippedAt));
    }

    /// <summary>The catalogue's answer, as the store's catalogue was imported (8002's single variant got ours).</summary>
    private sealed class CatalogueSkus : ISourceVariantSkus
    {
        private static readonly Dictionary<string, string> Imported = new() { ["44001"] = "MOKA-OLV", ["44003"] = "8002-44003" };

        public Task<IReadOnlyDictionary<string, string>> SkusOfAsync(string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                externalVariantIds.Where(Imported.ContainsKey).ToDictionary(id => id, id => Imported[id]));
    }
}
