using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The dashboard's arithmetic (P11-7), with no database: what counts as a sale,
/// what is in hand and what is late, how the funnel converts, and what agents bring
/// — rehearsals left out of every figure but their own.
/// </summary>
public sealed class DashboardFiguresTests
{
    private static readonly TestClock Clock = new();

    private static OrderLine Line(string sku, decimal price, int quantity) =>
        new(ProductId.New(), VariantId.New(), sku, sku, null, null, new Money(price, "EUR"), quantity);

    private static Order Sale(params OrderLine[] lines) => OrderBuilder.Default().WithLines(lines).At(Clock).InStatus(OrderStatus.Confirmed);

    [Fact]
    public void A_sale_is_a_live_order_that_was_paid_for_and_not_cancelled()
    {
        Assert.True(DashboardFigures.IsSale(OrderBuilder.Default().InStatus(OrderStatus.PaymentAuthorized)));
        Assert.True(DashboardFigures.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Delivered)));
        Assert.False(DashboardFigures.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Pending)));
        Assert.False(DashboardFigures.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Cancelled)));
        Assert.False(DashboardFigures.IsSale(OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed)));
    }

    [Fact]
    public void Sales_sum_revenue_orders_and_units_against_the_previous_period_and_rank_what_sold()
    {
        // 2 × 10 + 4.95 shipping, and 1 × 30 + 3 × 5 + 4.95.
        List<Order> current = [Sale(Line("A", 10m, 2)), Sale(Line("B", 30m, 1), Line("C", 5m, 3)), OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed)];
        List<Order> previous = [Sale(Line("A", 10m, 1))];

        var sales = DashboardFigures.Sales(DashboardPeriod.Week, current, previous);

        Assert.Equal(("week", "EUR"), (sales.Period, sales.Currency));
        Assert.Equal(new SalesFigures(74.90m, 2, 6, 37.45m), sales.Current);
        Assert.Equal(new SalesFigures(14.95m, 1, 1, 14.95m), sales.Previous);
        Assert.Equal(["C", "A", "B"], sales.BestSellers.Select(seller => seller.Sku));
        Assert.Equal(new BestSeller("C", "C", 3, 15m), sales.BestSellers[0]);
    }

    [Fact]
    public void Nothing_sold_is_zero_everywhere_and_not_a_division_error()
    {
        var sales = DashboardFigures.Sales(DashboardPeriod.Day, [], []);
        var window = DashboardWindow.Ending(Clock.GetUtcNow(), DashboardPeriod.Day);
        var operations = DashboardFigures.Operations(DashboardPeriod.Day, window, [], [], []);

        Assert.Equal(new SalesFigures(0m, 0, 0, 0m), sales.Current);
        Assert.Empty(sales.BestSellers);
        Assert.Equal((0m, 0m, "EUR"), (operations.CancellationRate, operations.ReturnRate, operations.Currency));
    }

    /// <summary>
    /// A best seller sums its units and its money over every order it appears in,
    /// ties are broken by money and then by SKU, and the shop's currency is the
    /// orders' — found by Stryker at phase 11's close, where every case was one order each.
    /// </summary>
    [Fact]
    public void Best_sellers_add_up_across_orders_and_break_ties_by_money_then_sku()
    {
        static OrderLine Dollars(string sku, decimal price, int quantity) =>
            new(ProductId.New(), VariantId.New(), sku, sku, null, null, new Money(price, "USD"), quantity);
        static Order Sold(params OrderLine[] lines) => OrderBuilder.Default().WithLines(lines)
            .WithShipping(new OrderShipping("standard", "Standard", new Money(0m, "USD"), 3)).InStatus(OrderStatus.Confirmed);

        var sales = DashboardFigures.Sales(DashboardPeriod.Week,
            [Sold(Dollars("B", 10m, 1)), Sold(Dollars("B", 10m, 2)), Sold(Dollars("C", 20m, 3)), Sold(Dollars("A", 20m, 3)), Sold(Dollars("D", 1m, 3))],
            []);

        Assert.Equal("USD", sales.Currency);
        Assert.Equal(["A", "C", "B", "D"], sales.BestSellers.Select(seller => seller.Sku));
        Assert.Equal(new BestSeller("B", "B", 3, 30m), sales.BestSellers[2]);
    }

    [Fact]
    public void Operations_count_what_is_in_hand_what_is_late_and_what_came_back()
    {
        var clock = new TestClock();
        var early = new TestClock(clock.GetUtcNow().AddDays(-30));
        var outsideReturn = ReturnRequest.Open(early, OrderBuilder.Default().WithLines(Line("OLD", 5m, 1)).At(early).InStatus(OrderStatus.Delivered),
            [new(VariantId.New(), "OLD", 1, ReturnReason.Damaged, null)], earlier: []);
        outsideReturn.Approve(early);
        outsideReturn.Receive(early);
        outsideReturn.Refund(early, new Money(5m, "EUR"), "ref-old");
        var late = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Confirmed);
        clock.Advance(TimeSpan.FromHours(1));
        var justLate = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Confirmed);
        clock.Advance(TimeSpan.FromHours(46));
        var onTime = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Confirmed);
        var failed = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentFailed);
        var authorised = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        var cancelled = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Cancelled);
        var rehearsal = OrderBuilder.Default().AsTest().At(clock).InStatus(OrderStatus.Confirmed);

        var delivered = OrderBuilder.Default().WithLines(Line("SHIRT", 20m, 4), Line("PANS", 10m, 4)).At(clock).InStatus(OrderStatus.Delivered);
        var open = ReturnRequest.Open(clock, delivered,
            [new(delivered.Lines[0].VariantId, "SHIRT", 2, ReturnReason.WrongSize, null), new(delivered.Lines[1].VariantId, "PANS", 1, ReturnReason.WrongSize, null)], earlier: []);
        var damaged = ReturnRequest.Open(clock, delivered,
            [new(delivered.Lines[1].VariantId, "PANS", 1, ReturnReason.Damaged, null), new(delivered.Lines[0].VariantId, "SHIRT", 1, ReturnReason.ChangedMind, null)], earlier: [open]);
        var refunded = ReturnRequest.Open(clock, delivered, [new(delivered.Lines[0].VariantId, "SHIRT", 1, ReturnReason.NotAsDescribed, null)], earlier: [open, damaged]);
        refunded.Approve(clock);
        refunded.Receive(clock);
        refunded.Refund(clock, new Money(20m, "EUR"), "ref-1");
        var alsoRefunded = ReturnRequest.Open(clock, delivered, [new(delivered.Lines[1].VariantId, "PANS", 1, ReturnReason.NotAsDescribed, null)], earlier: [open, damaged, refunded]);
        alsoRefunded.Approve(clock);
        alsoRefunded.Receive(clock);
        alsoRefunded.Refund(clock, new Money(10m, "EUR"), "ref-2");

        // Exactly 48 hours after `late` was confirmed: `late` is late, `justLate` is not yet.
        clock.Advance(TimeSpan.FromHours(1));
        var window = DashboardWindow.Ending(clock.GetUtcNow(), DashboardPeriod.Week);
        var ops = DashboardFigures.Operations(DashboardPeriod.Week, window,
            placed: [late, justLate, onTime, failed, authorised, cancelled, rehearsal, delivered],
            inHand: [late, justLate, onTime, failed, authorised, rehearsal],
            returns: [open, damaged, refunded, alsoRefunded, outsideReturn]);

        Assert.Equal((3, 1, 1, 1), (ops.ToShip, ops.LateToShip, ops.InProgress, ops.PaymentFailed));
        Assert.Equal((1, 0.1429m), (ops.Cancelled, ops.CancellationRate));
        Assert.Equal(2, ops.OpenReturns);
        // Units per reason, the most first: 3 wrong size (two lines), 1 damaged.
        // Equal counts are ordered by name: changed_mind before damaged.
        Assert.Equal([new ReasonCount("wrong_size", 3), new ReasonCount("changed_mind", 1), new ReasonCount("damaged", 1)], ops.OpenReturnsByReason);
        // Refunds inside the window only; returns opened in it against orders placed in it.
        Assert.Equal((30m, 0.5714m), (ops.Refunded, ops.ReturnRate));
    }

    [Fact]
    public void The_funnel_converts_carts_into_orders_and_tells_new_customers_from_returning_ones()
    {
        var clock = new TestClock();
        var later = new TestClock(clock.GetUtcNow().AddDays(3));
        Cart Started()
        {
            var cart = Cart.Start(clock, "es", "EUR");
            cart.Add(clock, new CartLine(ProductId.New(), VariantId.New(), "SKU-1", LocalizedText.From("es", "Algo"), null, null, 1));
            return cart;
        }

        var checkedOut = Started();
        checkedOut.MarkCheckedOut(clock);
        var abandoned = Started();
        abandoned.Abandon(clock);
        var expired = Started();
        var open = Cart.Start(later, "es", "EUR");

        var anna = CustomerId.New();
        var ben = CustomerId.New();
        var cleo = CustomerId.New();
        List<Order> placed =
        [
            OrderBuilder.Default().ForCustomer(anna).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().ForCustomer(anna).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().ForCustomer(ben).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().ForCustomer(cleo).InStatus(OrderStatus.Confirmed),
        ];

        var funnel = DashboardFigures.Funnel(DashboardPeriod.Week, expired.ExpiresAt, [checkedOut, abandoned, expired, open], placed, new HashSet<CustomerId> { ben });

        // A checked-out cart is not abandoned; an expired open one is; one still inside its life is open.
        Assert.Equal((4, 1, 2, 1, 0.25m), (funnel.CartsStarted, funnel.CheckedOut, funnel.Abandoned, funnel.StillOpen, funnel.Conversion));
        Assert.Equal((2, 1), (funnel.NewCustomers, funnel.ReturningCustomers));
    }

    [Fact]
    public void Agents_share_of_sales_counts_mandates_and_keeps_rehearsals_apart()
    {
        var mandate = new OrderMandate("mandate.checkout.1", "platform-key", "hash", 0m, "EUR", null);
        List<Order> placed =
        [
            OrderBuilder.Default().InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("claude-desktop", mandate).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("claude-desktop", mandate).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("claude-desktop").InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("a-small-agent", mandate).WithLines(Line("X", 5m, 1)).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("b-small-agent").WithLines(Line("X", 5m, 1)).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("other-agent").InStatus(OrderStatus.Cancelled),
            OrderBuilder.Default().PlacedBy("claude-desktop-test").AsTest().InStatus(OrderStatus.Confirmed),
        ];

        var agents = DashboardFigures.Agents(DashboardPeriod.Month, placed);

        // Each default order is 79.95 + 4.95 = 84.90; the small ones 5 + 4.95. Three of
        // five agent orders carry a mandate, so "with" and "without" cannot be confused.
        Assert.Equal((6, 359.50m, 5, 274.60m, 3, 1), (agents.Orders, agents.Revenue, agents.AgentOrders, agents.AgentRevenue, agents.Mandated, agents.Rehearsals));
        // Equal money is ordered by name.
        Assert.Equal(
            [new AgentSalesRow("claude-desktop", 3, 254.70m, 2), new AgentSalesRow("a-small-agent", 1, 9.95m, 1), new AgentSalesRow("b-small-agent", 1, 9.95m, 0)],
            agents.ByAgent);
    }

    [Theory]
    [InlineData(null, DashboardPeriod.Week)]
    [InlineData("day", DashboardPeriod.Day)]
    [InlineData("MONTH", DashboardPeriod.Month)]
    [InlineData("year", null)]
    [InlineData("1", null)]
    public void A_period_is_one_of_three_and_a_week_by_default(string? given, DashboardPeriod? expected) =>
        Assert.Equal(expected, DashboardPeriods.Parse(given));

    /// <summary>
    /// Health against a target (the redesign's R-4.5), on the boundaries that
    /// decide it: the last fifth up to the target, the target included, is near,
    /// and only above it is outside.
    /// </summary>
    [Theory]
    [InlineData("0", "within")]
    [InlineData("0.02", "within")]
    [InlineData("0.0201", "near")]
    [InlineData("0.025", "near")]
    [InlineData("0.0251", "outside")]
    public void Says_where_a_rate_stands_against_its_target(string rate, string standing)
    {
        // As text: a double would arrive as 0.025000000000000001 and land on the wrong side.
        Assert.Equal(standing, DashboardFigures.Standing(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), DashboardFigures.CancellationTarget));
    }
}
