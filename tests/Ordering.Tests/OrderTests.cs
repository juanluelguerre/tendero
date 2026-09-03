using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

public sealed class OrderTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void An_order_keeps_the_currency_it_was_placed_in()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"), OrderBuilder.Line(5m, "EUR"))
            .Build();

        Assert.Equal("EUR", order.Currency);
    }

    [Fact]
    public void The_lines_total_is_the_sum_of_the_lines()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR", quantity: 3), OrderBuilder.Line(5.50m, "EUR"))
            .WithShipping(new OrderShipping("free", "Free", Money.Zero("EUR"), 3))
            .Build();

        Assert.Equal(new Money(35.50m, "EUR"), order.LinesTotal);
    }

    /// <summary>
    /// The order total is NOT the sum of the lines, and this is the test that
    /// says so. It used to be — harmless while an order had no discounts, no
    /// shipping and no tax, and wrong the moment it did.
    ///
    /// Everything monetary is frozen from the quote (ADR 0016). An aggregate
    /// that recomputed its own total would be a second pricing engine, and the
    /// day the two disagreed the customer would be right and both would be wrong.
    /// </summary>
    [Fact]
    public void The_order_total_is_the_frozen_one_and_not_a_recomputation()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR", quantity: 3))
            .WithShipping(new OrderShipping("standard", "Standard", new Money(4.95m, "EUR"), 3))
            .WithTotals(new OrderTotals(
                Subtotal: new Money(30m, "EUR"),
                DiscountTotal: new Money(3m, "EUR"),
                Shipping: new Money(4.95m, "EUR"),
                TaxTotal: new Money(6.71m, "EUR"),
                Total: new Money(38.66m, "EUR")))
            .Build();

        Assert.Equal(new Money(30m, "EUR"), order.LinesTotal);
        Assert.Equal(new Money(38.66m, "EUR"), order.Total);
    }

    /// <summary>
    /// The totals arrive from somewhere else, so their currency is an assumption
    /// until it is checked. A total in the wrong currency reads fine and charges
    /// a hundred times over.
    /// </summary>
    [Fact]
    public void An_order_whose_totals_are_in_another_currency_is_rejected()
    {
        var builder = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"))
            .WithTotals(new OrderTotals(
                new Money(10m, "USD"), Money.Zero("USD"), Money.Zero("USD"),
                Money.Zero("USD"), new Money(10m, "USD")));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("totals are not", exception.Message);
    }

    /// <summary>
    /// Inventory speaks in SKUs and knows nothing about lines, so the order
    /// answers the question rather than making the saga group for itself — and
    /// the same question is asked again when a return restocks.
    /// </summary>
    [Fact]
    public void Two_lines_of_one_sku_are_one_question_to_inventory()
    {
        var line = OrderBuilder.Line(10m, "EUR", quantity: 2);

        var order = OrderBuilder.Default()
            .WithLines(line, line with { Quantity = 3 }, OrderBuilder.Line(5m, "EUR"))
            .Build();

        Assert.Equal(5, order.SkuQuantities().Single(pair => pair.Sku == line.Sku).Quantity);
    }

    [Fact]
    public void Placing_an_order_that_mixes_currencies_is_rejected()
    {
        var builder = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"), OrderBuilder.Line(12m, "USD"))
            // Totals in EUR, so the currency check that fires is the one about
            // the LINES rather than the one about the totals.
            .WithTotals(new OrderTotals(
                new Money(22m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"),
                Money.Zero("EUR"), new Money(22m, "EUR")));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("mix currencies", exception.Message);
    }

    /// <summary>
    /// The currency lives on the order, not on the first line. Without this, an
    /// order with no lines blew Total up with an ArgumentOutOfRange from inside
    /// the aggregate — unreachable today, but with the fuse already lit.
    /// </summary>
    [Fact]
    public void The_lines_total_of_an_order_with_no_lines_is_zero_in_its_own_currency()
    {
        // The private constructor is the one EF uses when materialising; going in
        // that way is the only way to build the state in question, because no
        // public method leaves an order with no lines.
        var materialised = (Order)Activator.CreateInstance(typeof(Order), nonPublic: true)!;
        typeof(Order).GetProperty(nameof(Order.Currency))!.SetValue(materialised, "EUR");

        Assert.Empty(materialised.Lines);
        Assert.Equal(Money.Zero("EUR"), materialised.LinesTotal);
    }

    [Fact]
    public void Placing_an_order_with_no_lines_is_rejected()
    {
        var builder = OrderBuilder.Default().WithLines();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void The_culture_is_normalised_to_its_language()
    {
        var order = OrderBuilder.Default().InCulture("es-ES").Build();

        Assert.Equal("es", order.Culture);
    }

    [Fact]
    public void Delivering_an_order_records_that_it_happened()
    {
        // It was the state machine's only mute transition, and it turns out to be
        // the one that opens the returns window: phase 4's return-reason loop has
        // no other fact to hang from.
        var order = OrderBuilder.Default().Build();
        order.AuthorizePayment(Clock);
        order.Confirm(Clock);
        order.Ship(Clock);
        order.ClearDomainEvents();

        order.Deliver(Clock);

        Assert.Contains(order.DomainEvents, e => e is OrderDelivered);
    }

    [Fact]
    public void Every_reachable_transition_records_a_domain_event()
    {
        // A transition with no event is a state change the rest of the system
        // cannot see: the outbox carries nothing and no worker wakes up.
        var order = OrderBuilder.Default().Build();

        foreach (var step in new Action[]
                 { () => order.AuthorizePayment(Clock), () => order.Confirm(Clock),
                   () => order.Ship(Clock), () => order.Deliver(Clock) })
        {
            order.ClearDomainEvents();
            step();
            Assert.NotEmpty(order.DomainEvents);
        }
    }
}
