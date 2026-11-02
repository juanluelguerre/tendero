using ElGuerre.Tendero.Ordering.Ports;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// The store's orders read as a sales channel's (phase 16, S3; ADR 0070): the ones
/// changed since the reconciliation's mark, oldest change first, and one by the id a
/// webhook names — both through the import's mapping, so a received order and an
/// imported one are read the same way.
/// </summary>
public sealed class ShopifyChannelOrdersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Orders_changed_since_a_mark_are_asked_for_by_their_change_and_carry_it()
    {
        var (connector, store) = ShopifyOrderConnectorTests.AConnector();
        var since = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var orders = new List<SourceOrder>();
        await foreach (var order in connector.UpdatedSinceAsync(since, Ct))
            orders.Add(order);

        var query = store.Queries.First(asked => asked.Contains("{ orders(", StringComparison.Ordinal));
        Assert.Contains("sortKey: UPDATED_AT", query, StringComparison.Ordinal);
        Assert.Contains("updated_at:>='2026-04-01T00:00:00Z'", query, StringComparison.Ordinal);
        Assert.Equal(
            [("5001", new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero)), ("5002", new DateTimeOffset(2026, 5, 10, 13, 0, 0, TimeSpan.Zero))],
            orders.Take(2).Select(order => (order.ExternalId, order.UpdatedAt!.Value)));
    }

    /// <summary>A webhook names an order by its number or its gid: either is the same order.</summary>
    [Theory]
    [InlineData("5001")]
    [InlineData("gid://shopify/Order/5001")]
    public async Task One_order_is_read_by_the_id_a_webhook_names(string id)
    {
        var (connector, store) = ShopifyOrderConnectorTests.AConnector();

        var order = await connector.FindAsync(id, Ct);

        Assert.Contains("order(id: \"gid://shopify/Order/5001\")", store.Queries.Last(), StringComparison.Ordinal);
        Assert.Equal(("5001", "#1001"), (order?.ExternalId, order?.Number));
        Assert.Equal("MOKA-OLV", order?.Lines[0].Sku);
    }

    [Fact]
    public async Task An_order_the_store_does_not_have_is_none() =>
        Assert.Null(await ShopifyOrderConnectorTests.AConnector().Connector.FindAsync("999999", Ct));
}
