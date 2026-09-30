using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using NodaTime;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Sales by hour or by day, against the period before (R-9): the dashboard's first
/// series. A day is the shop's calendar day, not UTC's — an order at 23:30 UTC on
/// a Saturday is Sunday in Madrid (ADR 0046: the shop's clock, NodaTime's zones) —
/// and a bucket with nothing in it is a zero, so an empty week still draws as a week.
/// </summary>
public sealed class SalesSeriesTests
{
    private static readonly DateTimeZone Madrid = DateTimeZoneProviders.Tzdb["Europe/Madrid"];

    private static OrderLine Line(decimal price, int quantity) =>
        new(ProductId.New(), VariantId.New(), "SKU", "Product", null, null, new Money(price, "EUR"), quantity);

    private static Order SaleAt(DateTimeOffset at, decimal price = 10m, int quantity = 1) =>
        OrderBuilder.Default().WithLines(Line(price, quantity)).At(new TestClock(at)).InStatus(OrderStatus.Confirmed);

    [Fact]
    public void A_week_is_seven_calendar_days_ending_today_and_the_seven_before()
    {
        // Wednesday 14 October 2026, 10:00 in Madrid (08:00 UTC, summer time).
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        Assert.Equal(SeriesStep.Day, plan.Step);
        Assert.Equal(7, plan.Current.Count);
        Assert.Equal(7, plan.Previous.Count);
        // Thursday 8 October at local midnight (22:00 UTC the day before) opens the week.
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(2)), plan.Current[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.FromHours(2)), plan.Current[^1]);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(2)), plan.End);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)), plan.From);
    }

    [Fact]
    public void A_day_is_twenty_four_hours_ending_with_this_one()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 40, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Day, now, Madrid);

        Assert.Equal((SeriesStep.Hour, 24, 24), (plan.Step, plan.Current.Count, plan.Previous.Count));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), plan.Current[^1]);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), plan.End);
    }

    [Fact]
    public void A_month_is_thirty_days()
    {
        var plan = SeriesPlans.For(DashboardPeriod.Month, new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), Madrid);

        Assert.Equal((SeriesStep.Day, 30, 30), (plan.Step, plan.Current.Count, plan.Previous.Count));
    }

    [Fact]
    public void A_late_order_lands_on_the_readers_day()
    {
        // Saturday 10 October, 23:30 UTC = Sunday 11 October, 01:30 in Madrid.
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        var late = SaleAt(new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero));

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, [late]);

        var sunday = series.Current.Single(point => point.Start == new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal(1, sunday.Orders);
        Assert.Equal(1, series.Current.Sum(point => point.Orders));
    }

    [Fact]
    public void A_day_is_a_calendar_day_across_a_clock_change()
    {
        // Sunday 25 October 2026: Madrid goes from +02:00 to +01:00 at 03:00, a 25-hour day.
        var now = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        var sunday = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var monday = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(1));
        var at = plan.Current.ToList().IndexOf(sunday);
        Assert.True(at >= 0);
        Assert.Equal(monday, plan.Current[at + 1]);
        Assert.Equal(TimeSpan.FromHours(25), plan.Current[at + 1] - plan.Current[at]);
    }

    [Fact]
    public void Each_bucket_sums_its_sales_and_the_period_before_is_its_own()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        List<Order> orders =
        [
            SaleAt(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), 10m, 2),   // this Tuesday
            SaleAt(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero), 30m, 1),   // this Tuesday
            SaleAt(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero), 10m, 1),    // Tuesday the week before
        ];

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, orders);

        // 2 × 10 + 4.95 shipping, and 30 + 4.95.
        Assert.Equal(new SalesPoint(plan.Current[5], 59.90m, 2, 3, 0, 0m, 2, 0), series.Current[5]);
        Assert.Equal(new SalesPoint(plan.Previous[5], 14.95m, 1, 1, 0, 0m, 1, 0), series.Previous[5]);
        Assert.Equal(("EUR", "Europe/Madrid", SeriesStep.Day), (series.Currency, series.TimeZone, series.Step));
    }

    [Fact]
    public void Test_orders_and_unpaid_ones_are_not_sales()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        var at = new TestClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));
        List<Order> orders =
        [
            OrderBuilder.Default().AsTest().At(at).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().At(at).InStatus(OrderStatus.Pending),
            OrderBuilder.Default().At(at).InStatus(OrderStatus.Cancelled),
        ];

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, orders);

        Assert.All(series.Current, point => Assert.Equal(0, point.Orders));
    }

    [Fact]
    public void Empty_buckets_are_zero_not_missing()
    {
        var plan = SeriesPlans.For(DashboardPeriod.Week, new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), Madrid);

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, []);

        Assert.Equal(plan.Current, series.Current.Select(point => point.Start));
        Assert.All(series.Current.Concat(series.Previous), point => Assert.Equal(new SalesPoint(point.Start, 0m, 0, 0, 0, 0m, 0, 0), point));
        Assert.Equal("EUR", series.Currency);
    }

    [Fact]
    public void An_order_on_the_boundary_belongs_to_the_bucket_it_opens()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, [SaleAt(plan.Current[3])]);

        Assert.Equal(1, series.Current[3].Orders);
        Assert.Equal(0, series.Current[2].Orders);
    }

    /// <summary>
    /// What agents bought and who bought, day by day (R-9, D-7): a buyer is new on
    /// the first day they appear in the two periods, unless they had ordered before
    /// the series began, and returning on every day after.
    /// </summary>
    [Fact]
    public void A_series_point_counts_what_agents_bought_and_who_bought_for_the_first_time()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        var ana = CustomerId.New();
        var luis = CustomerId.New();
        var regular = CustomerId.New();
        Order By(CustomerId who, DateTimeOffset at, string? agent = null)
        {
            var builder = OrderBuilder.Default().ForCustomer(who).WithLines(Line(10m, 1)).At(new TestClock(at));
            return (agent is null ? builder : builder.PlacedBy(agent)).InStatus(OrderStatus.Confirmed);
        }
        var monday = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        var tuesday = monday.AddDays(1);
        List<Order> orders =
        [
            By(ana, monday),
            By(regular, monday),
            By(ana, tuesday, agent: "agent-1"),
            By(luis, tuesday, agent: "agent-1"),
        ];

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, orders, orderedBefore: new HashSet<CustomerId> { regular });

        Assert.Equal((0, 0m, 1, 1), (series.Current[4].AgentOrders, series.Current[4].AgentRevenue, series.Current[4].NewCustomers, series.Current[4].ReturningCustomers));
        Assert.Equal((2, 29.90m, 1, 1), (series.Current[5].AgentOrders, series.Current[5].AgentRevenue, series.Current[5].NewCustomers, series.Current[5].ReturningCustomers));
    }
}
