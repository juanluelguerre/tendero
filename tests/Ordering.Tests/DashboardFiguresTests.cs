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

        Assert.Equal(new SalesFigures(0m, 0, 0, 0m), sales.Current);
        Assert.Empty(sales.BestSellers);
    }

    [Fact]
    public void Operations_count_what_is_in_hand_what_is_late_and_what_came_back()
    {
        var clock = new TestClock();
        var late = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Confirmed);
        clock.Advance(TimeSpan.FromHours(47));
        var onTime = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Confirmed);
        var failed = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentFailed);
        var authorised = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);
        var cancelled = OrderBuilder.Default().At(clock).InStatus(OrderStatus.Cancelled);
        var rehearsal = OrderBuilder.Default().AsTest().At(clock).InStatus(OrderStatus.Confirmed);

        var delivered = OrderBuilder.Default().WithLines(Line("SHIRT", 20m, 3)).At(clock).InStatus(OrderStatus.Delivered);
        var open = ReturnRequest.Open(clock, delivered, [new(delivered.Lines[0].VariantId, "SHIRT", 2, ReturnReason.WrongSize, null)], earlier: []);
        var refunded = ReturnRequest.Open(clock, delivered, [new(delivered.Lines[0].VariantId, "SHIRT", 1, ReturnReason.NotAsDescribed, null)], earlier: [open]);
        refunded.Approve(clock);
        refunded.Receive(clock);
        refunded.Refund(clock, new Money(20m, "EUR"), "ref-1");

        clock.Advance(TimeSpan.FromHours(2));
        var window = DashboardWindow.Ending(clock.GetUtcNow(), DashboardPeriod.Week);
        var ops = DashboardFigures.Operations(DashboardPeriod.Week, window,
            placed: [late, onTime, failed, authorised, cancelled, rehearsal, delivered],
            inHand: [late, onTime, failed, authorised, rehearsal],
            returns: [open, refunded]);

        Assert.Equal((2, 1, 1, 1), (ops.ToShip, ops.LateToShip, ops.InProgress, ops.PaymentFailed));
        Assert.Equal((1, 0.1667m), (ops.Cancelled, ops.CancellationRate));
        Assert.Equal(1, ops.OpenReturns);
        Assert.Equal([new ReasonCount("wrong_size", 2)], ops.OpenReturnsByReason);
        Assert.Equal((20m, 0.3333m), (ops.Refunded, ops.ReturnRate));
    }

    [Fact]
    public void The_funnel_converts_carts_into_orders_and_tells_new_customers_from_returning_ones()
    {
        var clock = new TestClock();
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
        var open = Started();

        var anna = CustomerId.New();
        var ben = CustomerId.New();
        List<Order> placed =
        [
            OrderBuilder.Default().ForCustomer(anna).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().ForCustomer(anna).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().ForCustomer(ben).InStatus(OrderStatus.Confirmed),
        ];

        var funnel = DashboardFigures.Funnel(DashboardPeriod.Week, expired.ExpiresAt, [checkedOut, abandoned, expired, open], placed, new HashSet<CustomerId> { ben });

        Assert.Equal((4, 1, 3, 0, 0.25m), (funnel.CartsStarted, funnel.CheckedOut, funnel.Abandoned, funnel.StillOpen, funnel.Conversion));
        Assert.Equal((1, 1), (funnel.NewCustomers, funnel.ReturningCustomers));
    }

    [Fact]
    public void Agents_share_of_sales_counts_mandates_and_keeps_rehearsals_apart()
    {
        var mandate = new OrderMandate("mandate.checkout.1", "platform-key", "hash", 0m, "EUR", null);
        List<Order> placed =
        [
            OrderBuilder.Default().InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("claude-desktop", mandate).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("claude-desktop").InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().PlacedBy("other-agent").InStatus(OrderStatus.Cancelled),
            OrderBuilder.Default().PlacedBy("claude-desktop-test").AsTest().InStatus(OrderStatus.Confirmed),
        ];

        var agents = DashboardFigures.Agents(DashboardPeriod.Month, placed);

        // Each default order is 79.95 + 4.95.
        Assert.Equal((3, 254.70m, 2, 169.80m, 1, 1), (agents.Orders, agents.Revenue, agents.AgentOrders, agents.AgentRevenue, agents.Mandated, agents.Rehearsals));
        Assert.Equal([new AgentSalesRow("claude-desktop", 2, 169.80m, 1)], agents.ByAgent);
    }

    [Theory]
    [InlineData(null, DashboardPeriod.Week)]
    [InlineData("day", DashboardPeriod.Day)]
    [InlineData("MONTH", DashboardPeriod.Month)]
    [InlineData("year", null)]
    [InlineData("1", null)]
    public void A_period_is_one_of_three_and_a_week_by_default(string? given, DashboardPeriod? expected) =>
        Assert.Equal(expected, DashboardPeriods.Parse(given));
}
