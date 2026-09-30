using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The facts the dashboard reads are recorded when they happen (R-9, D-7): the
/// channel an order came through, the moments it was confirmed and shipped, and
/// the category each line was sold in. An order placed before any of this has
/// none of them, which is the honest answer.
/// </summary>
public sealed class OrderMomentsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_order_remembers_when_it_was_confirmed_and_when_it_shipped()
    {
        var clock = new TestClock(Morning);
        var order = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);

        order.Confirm(clock);
        clock.Advance(TimeSpan.FromHours(30));
        order.Ship(clock);

        Assert.Equal((Morning, Morning.AddHours(30)), (order.ConfirmedAt, order.ShippedAt));
    }

    [Fact]
    public void An_order_not_yet_confirmed_has_no_moments()
    {
        var order = OrderBuilder.Default().InStatus(OrderStatus.PaymentAuthorized);

        Assert.Equal((null, null), (order.ConfirmedAt, order.ShippedAt));
    }

    [Fact]
    public void Each_line_keeps_the_category_it_was_sold_in()
    {
        var line = OrderBuilder.Line(10m, "EUR") with { CategoryCode = "COOKWARE" };

        Assert.Equal("COOKWARE", OrderBuilder.Default().WithLines(line).Build().Lines[0].CategoryCode);
    }

    [Fact]
    public void An_order_keeps_the_channel_it_came_through()
    {
        Assert.Equal(OrderChannel.Mcp, OrderBuilder.Default().Through(OrderChannel.Mcp).Build().Channel);
    }

    [Theory]
    [InlineData(Surfaces.Web, OrderChannel.Web)]
    [InlineData(Surfaces.Mcp, OrderChannel.Mcp)]
    [InlineData(Surfaces.Ucp, OrderChannel.Ucp)]
    public void A_surface_names_its_channel(string surface, OrderChannel expected) =>
        Assert.Equal(expected, OrderChannels.From(surface));

    /// <summary>The worker places nothing; an internal surface is no channel, not the web.</summary>
    [Fact]
    public void An_internal_surface_is_no_channel() => Assert.Null(OrderChannels.From(Surfaces.Internal));
}
