using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// An order a sales channel took (phase 16, S3; ADR 0070): not history, a live order
/// Tendero must ship. It enters the state machine as any order does and moves at once
/// to authorised, the payment being one the channel already took — so the saga holds
/// its stock, confirms it, and never captures or voids money that is not Tendero's.
/// One whose stock cannot be held is kept and flagged oversold, never cancelled.
/// </summary>
public sealed class ReceivedOrderTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private static readonly OrderOrigin Shopify = new("shopify", "gid://shopify/Order/7001", "#1042");
    private static readonly DateTimeOffset PlacedThere = new(2026, 10, 31, 11, 58, 0, TimeSpan.Zero);

    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");
    private static readonly OrderShipping Standard = new("shopify:shipping", "Envío estándar", new Money(4.95m, "EUR"), null);

    private static OrderLine Line() =>
        new(ProductId.New(), VariantId.New(), "TAZ-001-BLU", "Taza de gres", "Azul", null, new Money(12.50m, "EUR"), 2);

    private static readonly OrderTotals Totals =
        new(new Money(25m, "EUR"), Money.Zero("EUR"), new Money(4.95m, "EUR"), new Money(5.20m, "EUR"), new Money(29.95m, "EUR"));

    private static Order Received() =>
        Order.Received(Clock, Shopify, OrderChannel.Shopify, CustomerId.New(), [Line()], Ana, Ana, Standard, Totals, PlacedThere,
            taxes: [new OrderTax("IVA", 0.21m, new Money(24.75m, "EUR"), new Money(5.20m, "EUR"))],
            contactEmail: "ana@example.com");

    [Fact]
    public void A_received_order_is_authorised_with_the_payment_the_channel_took_and_says_where_it_came_from()
    {
        var order = Received();

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal(OrderChannel.Shopify, order.Channel);
        Assert.Equal(Shopify, order.ReceivedFrom);
        Assert.Null(order.ImportedFrom);
        Assert.Equal("channel:shopify:gid://shopify/Order/7001", order.IdempotencyKey);
        Assert.Equal(PlacedThere, order.CreatedAt);
        Assert.Equal(new Money(29.95m, "EUR"), order.Total);
        Assert.Equal("ana@example.com", order.ContactEmail);

        // Captured already: the saga's capture on shipping and void on cancelling both pass it by.
        Assert.Equal(new OrderPayment("shopify", Shopify.ExternalId, Shopify.ExternalId), order.Payment);
    }

    /// <summary>Placed and authorised: the saga holds its stock and confirms it, as for any order.</summary>
    [Fact]
    public void A_received_order_raises_what_the_saga_listens_to()
    {
        var order = Received();

        Assert.Equal(
            [typeof(OrderPlaced), typeof(OrderPaymentAuthorized)],
            order.DomainEvents.Select(raised => raised.GetType()));
    }

    /// <summary>
    /// The channel took the money for stock Tendero did not have: the order stays, marked,
    /// for a person to act on — cancelling it would hide what the margin exists to bound.
    /// </summary>
    [Fact]
    public void A_received_order_whose_stock_cannot_be_held_is_flagged_oversold_and_not_cancelled()
    {
        var order = Received();
        order.ClearDomainEvents();

        order.MarkOversold(Clock, "Only 1 of TAZ-001-BLU left, 2 wanted.");

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal("Only 1 of TAZ-001-BLU left, 2 wanted.", order.Oversold);
        Assert.IsType<OrderOversold>(Assert.Single(order.DomainEvents));
    }

    [Fact]
    public void Marking_oversold_twice_says_it_once()
    {
        var order = Received();
        order.MarkOversold(Clock, "short");
        order.ClearDomainEvents();

        order.MarkOversold(Clock, "short");

        Assert.Empty(order.DomainEvents);
    }

    /// <summary>Only an order a channel took can be oversold: one placed here was refused at checkout instead.</summary>
    [Fact]
    public void An_order_placed_here_cannot_be_flagged_oversold() =>
        Assert.Throws<InvalidOperationException>(() => OrderBuilder.Default().Build().MarkOversold(Clock, "short"));

    [Fact]
    public void A_received_order_needs_figures_that_add_up() =>
        Assert.Throws<InvalidOperationException>(() => Order.Received(
            Clock, Shopify, OrderChannel.Shopify, CustomerId.New(), [Line()], Ana, Ana, Standard,
            Totals with { Total = new Money(99m, "EUR") }, PlacedThere));
}
