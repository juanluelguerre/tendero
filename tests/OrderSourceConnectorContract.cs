using ElGuerre.Tendero.Ordering.Ports;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests.Sources;

/// <summary>
/// The orders port's executable contract (phase 16, S2b): every order source
/// inherits this class and passes the same suite. Linked as a file into each test
/// project that has one (Ordering.Tests for its fake, Shopify.Tests for Shopify),
/// like the customers' and the stock's suites.
/// </summary>
public abstract class OrderSourceConnectorContractTests
{
    protected abstract IOrderSourceConnector CreateConnector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<SourceOrder>> All()
    {
        var orders = new List<SourceOrder>();
        await foreach (var order in CreateConnector().StreamOrdersAsync(Ct))
            orders.Add(order);
        return orders;
    }

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    /// <summary>Each order once, by an id of the source's: what a re-import finds it by.</summary>
    [Fact]
    public async Task Orders_have_an_id_and_no_id_twice()
    {
        var orders = await All();

        Assert.NotEmpty(orders);
        Assert.All(orders, order => Assert.False(string.IsNullOrWhiteSpace(order.ExternalId)));
        Assert.Equal(orders.Count, orders.Select(order => order.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A line has an id a refund can name it by, something bought, and no line id is used twice in an order.</summary>
    [Fact]
    public async Task An_orders_lines_are_named_and_bought() =>
        Assert.All(await All(), order =>
        {
            Assert.NotEmpty(order.Lines);
            Assert.All(order.Lines, line => Assert.True(line.Quantity > 0 && !string.IsNullOrWhiteSpace(line.ExternalId), order.Number));
            Assert.Equal(order.Lines.Count, order.Lines.Select(line => line.ExternalId).Distinct(StringComparer.Ordinal).Count());
        });

    /// <summary>One currency an order: its figures, its lines and its refunds are all in the order's.</summary>
    [Fact]
    public async Task An_order_is_in_one_currency() =>
        Assert.All(await All(), order =>
        {
            var currency = order.Totals.Total.Currency;
            Assert.All(
                order.Lines.Select(line => line.UnitPrice.Currency)
                    .Concat([order.Totals.Subtotal.Currency, order.Totals.Discount.Currency, order.Totals.Shipping.Currency, order.Totals.Tax.Currency, order.Shipping.Amount.Currency])
                    .Concat(order.Refunds.Select(refund => refund.Amount.Currency)),
                each => Assert.Equal(currency, each));
        });

    /// <summary>A refund takes back lines of its own order, each at least once.</summary>
    [Fact]
    public async Task A_refund_names_its_orders_lines() =>
        Assert.All(await All(), order =>
        {
            var lines = order.Lines.Select(line => line.ExternalId).ToHashSet(StringComparer.Ordinal);
            Assert.All(order.Refunds.SelectMany(refund => refund.Lines), line =>
                Assert.True(lines.Contains(line.LineExternalId) && line.Quantity > 0, order.Number));
        });

    /// <summary>Only a fulfilled order says when it shipped, and every fulfilled one does: the returns window counts from it.</summary>
    [Fact]
    public async Task Only_a_fulfilled_order_has_shipped() =>
        Assert.All(await All(), order =>
            Assert.Equal(order.State == SourceOrderState.Fulfilled, order.ShippedAt is not null));
}
