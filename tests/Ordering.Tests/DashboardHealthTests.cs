using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What the large back offices measure and the shop did not (R-9, D-7): units per
/// order, the order defect rate against 1 % and the late shipment rate against
/// 4 % — each counted from facts the order records, and each saying *no data*
/// rather than 0 % when there is nothing to divide.
/// </summary>
public sealed class DashboardHealthTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    private static OrderLine Line(string sku, int quantity) =>
        new(ProductId.New(), VariantId.New(), sku, sku, null, null, new Money(10m, "EUR"), quantity);

    /// <summary>The week ending a minute after the last thing happened: the window's end is exclusive.</summary>
    private static DashboardWindow Window(TestClock clock) => DashboardWindow.Ending(clock.GetUtcNow().AddMinutes(1), DashboardPeriod.Week);

    [Fact]
    public void Units_per_order_is_units_over_orders()
    {
        var clock = new TestClock(Start);
        List<Order> sales =
        [
            OrderBuilder.Default().WithLines(Line("A", 1)).At(clock).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().WithLines(Line("B", 2)).At(clock).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().WithLines(Line("C", 4)).At(clock).InStatus(OrderStatus.Confirmed),
        ];

        Assert.Equal(2.33m, DashboardFigures.Sales(DashboardPeriod.Week, sales, []).Current.UnitsPerOrder);
    }

    /// <summary>
    /// A defect is what the shopper was let down by: a return for damaged or not
    /// as described, or an order the shop cancelled. A changed mind is a return,
    /// not a defect; a claim the shop rejected or the shopper withdrew is neither.
    /// </summary>
    [Fact]
    public void A_damaged_return_or_a_shop_cancellation_is_a_defect_and_a_changed_mind_is_not()
    {
        var clock = new TestClock(Start);
        var damaged = OrderBuilder.Default().WithLines(Line("PAN", 1)).At(clock).InStatus(OrderStatus.Delivered);
        var changedMind = OrderBuilder.Default().WithLines(Line("MUG", 1)).At(clock).InStatus(OrderStatus.Delivered);
        var cancelledByTheShop = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Cancelled);
        List<Order> fine = [.. Enumerable.Range(0, 7).Select(_ => OrderBuilder.Default().At(clock).InStatus(OrderStatus.Delivered))];
        clock.Advance(TimeSpan.FromHours(2));
        var returns = new[]
        {
            ReturnRequest.Open(clock, damaged, [new(damaged.Lines[0].VariantId, "PAN", 1, ReturnReason.Damaged, null)], earlier: []),
            ReturnRequest.Open(clock, changedMind, [new(changedMind.Lines[0].VariantId, "MUG", 1, ReturnReason.ChangedMind, null)], earlier: []),
        };

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock),
            placed: [damaged, changedMind, cancelledByTheShop, .. fine], inHand: [], returns: returns);

        Assert.Equal((2, 0.2m, "outside"), (ops.Defects, ops.DefectRate, ops.DefectStanding));
    }

    [Fact]
    public void A_period_with_no_orders_has_no_defect_rate_rather_than_zero()
    {
        var clock = new TestClock(Start);

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock), placed: [], inHand: [], returns: []);

        Assert.Equal((null, null, null, null), (ops.DefectRate, ops.DefectStanding, ops.LateShipmentRate, ops.LateShipmentStanding));
    }

    [Fact]
    public void An_order_shipped_more_than_48_hours_after_it_was_confirmed_is_late()
    {
        var clock = new TestClock(Start);
        var late = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        var onTime = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        var exactly = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        late.Confirm(clock);
        onTime.Confirm(clock);
        exactly.Confirm(clock);
        clock.Advance(TimeSpan.FromHours(10));
        onTime.Ship(clock);
        clock.Advance(TimeSpan.FromHours(38));
        exactly.Ship(clock);
        clock.Advance(TimeSpan.FromHours(1));
        late.Ship(clock);

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock),
            placed: [], inHand: [], returns: [], shipped: [late, onTime, exactly]);

        Assert.Equal((1, 3, 0.3333m, "outside"), (ops.LateShipments, ops.ShippedCounted, ops.LateShipmentRate, ops.LateShipmentStanding));
    }

    /// <summary>An order that shipped before the shop recorded when is not on time: it is not counted at all, and said so.</summary>
    [Fact]
    public void Orders_shipped_before_the_moments_were_recorded_are_counted_apart()
    {
        var clock = new TestClock(Start);
        var recorded = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        recorded.Confirm(clock);
        clock.Advance(TimeSpan.FromHours(5));
        recorded.Ship(clock);
        var unrecorded = Unrecorded(OrderBuilder.Default().At(clock).InStatus(OrderStatus.Shipped));

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock),
            placed: [], inHand: [], returns: [], shipped: [recorded, unrecorded]);

        Assert.Equal((1, 1, 0m), (ops.ShippedCounted, ops.ShippedBeforeMoments, ops.LateShipmentRate));
    }

    /// <summary>An order as the database holds one placed before the moments existed.</summary>
    private static Order Unrecorded(Order order)
    {
        typeof(Order).GetProperty(nameof(Order.ConfirmedAt))!.SetValue(order, null);
        typeof(Order).GetProperty(nameof(Order.ShippedAt))!.SetValue(order, null);
        return order;
    }
}
