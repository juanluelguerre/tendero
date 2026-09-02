using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

using ElGuerre.Tendero.Tests;

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
    public void The_total_is_the_sum_of_the_lines()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR", quantity: 3), OrderBuilder.Line(5.50m, "EUR"))
            .Build();

        Assert.Equal(new Money(35.50m, "EUR"), order.Total);
    }

    [Fact]
    public void Placing_an_order_that_mixes_currencies_is_rejected()
    {
        var builder = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"), OrderBuilder.Line(12m, "USD"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("mix currencies", exception.Message);
    }

    /// <summary>
    /// The currency lives on the order, not on the first line. Without this, an
    /// order with no lines blew Total up with an ArgumentOutOfRange from inside
    /// the aggregate — unreachable today, but with the fuse already lit.
    /// </summary>
    [Fact]
    public void The_total_of_an_order_with_no_lines_is_zero_in_its_own_currency()
    {
        // The private constructor is the one EF uses when materialising; going in
        // that way is the only way to build the state in question, because no
        // public method leaves an order with no lines.
        var materialised = (Order)Activator.CreateInstance(typeof(Order), nonPublic: true)!;
        typeof(Order).GetProperty(nameof(Order.Currency))!.SetValue(materialised, "EUR");

        Assert.Empty(materialised.Lines);
        Assert.Equal(Money.Zero("EUR"), materialised.Total);
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
