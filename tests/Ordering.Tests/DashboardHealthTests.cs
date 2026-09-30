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

    /// <summary>
    /// A return the shop rejected said nothing was wrong; one line damaged among
    /// others is still a damaged parcel.
    /// </summary>
    [Fact]
    public void A_rejected_return_is_no_defect_and_one_damaged_line_among_others_is()
    {
        var clock = new TestClock(Start);
        var rejected = OrderBuilder.Default().WithLines(Line("PAN", 1)).At(clock).InStatus(OrderStatus.Delivered);
        var mixed = OrderBuilder.Default().WithLines(Line("PAN", 1), Line("MUG", 1)).At(clock).InStatus(OrderStatus.Delivered);
        clock.Advance(TimeSpan.FromHours(2));
        var refused = ReturnRequest.Open(clock, rejected, [new(rejected.Lines[0].VariantId, "PAN", 1, ReturnReason.Damaged, null)], earlier: []);
        refused.Reject(clock, "The photo shows the pan as it was sold.");
        var returns = new[]
        {
            refused,
            ReturnRequest.Open(clock, mixed,
                [new(mixed.Lines[0].VariantId, "PAN", 1, ReturnReason.ChangedMind, null), new(mixed.Lines[1].VariantId, "MUG", 1, ReturnReason.Damaged, null)],
                earlier: []),
        };

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock), placed: [rejected, mixed], inHand: [], returns: returns);

        Assert.Equal(1, ops.Defects);
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

    /// <summary>
    /// The window is its start up to its end: a shipment at the first instant is in
    /// it, one at the end is the next window's — for the orders with their moments
    /// and for those counted apart, windowed by their last change.
    /// </summary>
    [Fact]
    public void A_shipment_at_the_window_s_start_is_in_it_and_one_at_its_end_is_not()
    {
        var clock = new TestClock(Start);
        Order ShippedAt(DateTimeOffset at, bool recorded)
        {
            var order = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
            order.Confirm(clock);
            order.Ship(new TestClock(at));
            return recorded ? order : Unrecorded(order);
        }
        var window = new DashboardWindow(Start.AddDays(1), Start.AddDays(8), Start.AddDays(-6));
        List<Order> shipped =
        [
            ShippedAt(window.From, recorded: true),
            ShippedAt(window.To, recorded: true),
            ShippedAt(window.From, recorded: false),
            ShippedAt(window.To, recorded: false),
        ];

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, window, placed: [], inHand: [], returns: [], shipped: shipped);

        Assert.Equal((1, 1), (ops.ShippedCounted, ops.ShippedBeforeMoments));
    }

    /// <summary>An order that knows when it shipped but not when it was confirmed has no lead time to measure: it is counted apart too.</summary>
    [Fact]
    public void An_order_with_only_one_of_its_moments_is_counted_apart()
    {
        var clock = new TestClock(Start);
        var halfRecorded = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        halfRecorded.Confirm(clock);
        clock.Advance(TimeSpan.FromHours(5));
        halfRecorded.Ship(clock);
        typeof(Order).GetProperty(nameof(Order.ConfirmedAt))!.SetValue(halfRecorded, null);

        var ops = DashboardFigures.Operations(DashboardPeriod.Week, Window(clock), placed: [], inHand: [], returns: [], shipped: [halfRecorded]);

        Assert.Equal((0, 1), (ops.ShippedCounted, ops.ShippedBeforeMoments));
    }

    /// <summary>An order as the database holds one placed before the moments existed.</summary>
    private static Order Unrecorded(Order order)
    {
        typeof(Order).GetProperty(nameof(Order.ConfirmedAt))!.SetValue(order, null);
        typeof(Order).GetProperty(nameof(Order.ShippedAt))!.SetValue(order, null);
        return order;
    }
}
