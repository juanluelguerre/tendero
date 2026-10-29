using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// An order that already happened somewhere else (phase 16, S2b, P16-18; ADR
/// 0066): born in its final state through the one named entry the table
/// declares, with every check placing makes and no event — nothing reserves,
/// captures or writes to anybody about a purchase made last year.
/// </summary>
public sealed class ImportedOrderTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private static readonly OrderOrigin Shopify = new("shopify", "5001", "#1001");
    private static readonly DateTimeOffset LastSpring = new(2026, 4, 2, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ShippedThen = new(2026, 4, 3, 9, 0, 0, TimeSpan.Zero);

    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");
    private static readonly OrderShipping Standard = new("shopify:standard", "Envío estándar", new Money(4.95m, "EUR"), null);

    private static OrderLine Line(decimal price = 79.95m, string currency = "EUR") =>
        new(ProductId.New(), VariantId.New(), "B073WXYZ01-38", "Zapatillas de running", "azul marino · 38", null, new Money(price, currency), 1);

    private static OrderTotals Totals(decimal subtotal = 79.95m, decimal total = 84.90m) =>
        new(new Money(subtotal, "EUR"), Money.Zero("EUR"), new Money(4.95m, "EUR"), new Money(14.73m, "EUR"), new Money(total, "EUR"));

    private static Order Imported(OrderStatus status, OrderLine? line = null, OrderTotals? totals = null) =>
        Order.Imported(
            Shopify, status, CustomerId.New(), [line ?? Line()], Ana, Ana, Standard, totals ?? Totals(), LastSpring,
            taxes: [new OrderTax("standard", 0.21m, new Money(70.17m, "EUR"), new Money(14.73m, "EUR"))],
            shippedAt: status == OrderStatus.Delivered ? ShippedThen : null,
            contactEmail: "ana@example.com");

    [Fact]
    public void A_delivered_order_is_born_delivered_on_the_day_it_was_placed_and_says_where_it_came_from()
    {
        var order = Imported(OrderStatus.Delivered);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal((LastSpring, ShippedThen), (order.CreatedAt, order.ShippedAt));
        Assert.Equal(Shopify, order.ImportedFrom);
        Assert.Equal("import:shopify:5001", order.IdempotencyKey);
        Assert.Equal(new Money(84.90m, "EUR"), order.Total);
        Assert.Equal("ana@example.com", order.ContactEmail);
    }

    /// <summary>Nothing listens to history: no stock reserved, no payment captured, no email.</summary>
    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void History_raises_no_event(OrderStatus status) =>
        Assert.Empty(Imported(status).DomainEvents);

    /// <summary>History is closed: an order still on its way is finished where it started, not brought half-walked.</summary>
    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PaymentAuthorized)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void An_order_still_on_its_way_is_refused(OrderStatus status) =>
        Assert.Throws<InvalidOperationException>(() => Imported(status));

    /// <summary>Born closed, it stays closed: the table has no edge out of a terminal state.</summary>
    [Fact]
    public void An_imported_order_cannot_be_moved()
    {
        var order = Imported(OrderStatus.Delivered);

        Assert.Throws<InvalidOperationException>(() => order.Cancel(Clock, OrderStop.ShopCancelled));
    }

    /// <summary>
    /// The returns window counts from the source's dates, not from the import: an
    /// order delivered last spring is not returnable for fourteen days because the
    /// shop moved platforms today.
    /// </summary>
    [Fact]
    public void An_order_delivered_long_ago_has_no_returns_window_open()
    {
        var order = Imported(OrderStatus.Delivered);

        Assert.Equal(ShippedThen, order.UpdatedAt);
        Assert.False(ReturnRequest.WindowIsOpen(order, Clock.GetUtcNow()));
    }

    /// <summary>The checks placing makes, kept: one currency, totals that add up.</summary>
    [Fact]
    public void What_placing_refuses_importing_refuses_too()
    {
        Assert.Throws<InvalidOperationException>(() => Imported(OrderStatus.Delivered, totals: Totals(total: 99m)));
        Assert.Throws<InvalidOperationException>(() => Imported(OrderStatus.Delivered, line: Line(currency: "USD")));
    }

    /// <summary>
    /// A line of nothing is no line, however the order is born. Found by phase 16
    /// S2b's Stryker close: "at most zero" made "below zero" survived, and so did
    /// "any line" made "every line".
    /// </summary>
    [Fact]
    public void A_line_of_nothing_is_refused()
    {
        var nothing = Line() with { Quantity = 0 };

        Assert.Throws<InvalidOperationException>(() => Order.Imported(
            Shopify, OrderStatus.Delivered, CustomerId.New(), [Line(), nothing], Ana, Ana, Standard,
            Totals(subtotal: 159.90m, total: 164.85m), LastSpring));
    }

    /// <summary>What the store took off is kept beside the figures it explains. Found by the same close.</summary>
    [Fact]
    public void An_imported_order_keeps_its_discounts()
    {
        var discount = new OrderDiscount("SPRING15", "SPRING15", new Money(12m, "EUR"));

        var order = Order.Imported(
            Shopify, OrderStatus.Delivered, CustomerId.New(), [Line()], Ana, Ana, Standard,
            new OrderTotals(new Money(79.95m, "EUR"), new Money(12m, "EUR"), new Money(4.95m, "EUR"), new Money(12.65m, "EUR"), new Money(72.90m, "EUR")),
            LastSpring, discounts: [discount]);

        Assert.Equal([discount], order.Discounts);
    }

    /// <summary>Placing still starts where it always did, and nowhere else.</summary>
    [Fact]
    public void An_order_placed_here_is_born_pending()
    {
        var order = OrderBuilder.Default().Build();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.ImportedFrom);
    }
}
