using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;
using static ElGuerre.Tendero.Ordering.Tests.Amounts;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The products that sell most and least in a period (S-3a): the ranking the home
/// uses, read from either end, narrowed to a category and everything under it.
/// </summary>
public sealed class ProductPerformanceTests
{
    private static readonly TestClock Clock = new();

    private static readonly ProductId Mug = ProductId.New();
    private static readonly ProductId Kettle = ProductId.New();
    private static readonly ProductId Scarf = ProductId.New();

    private static OrderLine Line(ProductId product, string name, int quantity, decimal price, string category, string? path = null) =>
        new(product, VariantId.New(), $"{name}-SKU", name, null, null, new Money(price, "EUR"), quantity, category, path);

    private static Order Sale(params OrderLine[] lines) =>
        OrderBuilder.Default().WithLines(lines).At(Clock).InStatus(OrderStatus.Confirmed);

    private static readonly IReadOnlyList<Order> Orders =
    [
        Sale(Line(Mug, "Mug", 5, 10m, "MUGS", "KITCHEN/MUGS"), Line(Scarf, "Scarf", 1, 30m, "SCARVES", "TEXTILES/SCARVES")),
        Sale(Line(Kettle, "Kettle", 2, 40m, "KETTLES", "KITCHEN/KETTLES")),
    ];

    [Fact]
    public void The_best_sell_most_units()
    {
        var best = ProductPerformanceFigures.Of(DashboardPeriod.Week, PerformanceRanking.Best, null, 10, Orders);

        Assert.Equal(["Mug", "Kettle", "Scarf"], best.Rows.Select(row => row.Name));
        Assert.Equal((5, Eur(50m)), (best.Rows[0].Units, best.Rows[0].Revenue));
        Assert.Equal(3, best.Total);
    }

    [Fact]
    public void The_worst_are_the_same_ranking_read_from_the_other_end()
    {
        var worst = ProductPerformanceFigures.Of(DashboardPeriod.Week, PerformanceRanking.Worst, null, 2, Orders);

        Assert.Equal(["Scarf", "Kettle"], worst.Rows.Select(row => row.Name));
        Assert.Equal(3, worst.Total);
    }

    /// <summary>A parent category counts every line sold under it (OrderLine.IsIn).</summary>
    [Fact]
    public void A_category_keeps_its_own_lines_and_its_childrens()
    {
        var kitchen = ProductPerformanceFigures.Of(DashboardPeriod.Week, PerformanceRanking.Best, "KITCHEN", 10, Orders);

        Assert.Equal(["Mug", "Kettle"], kitchen.Rows.Select(row => row.Name));
        Assert.Equal("KITCHEN", kitchen.Category);
    }

    [Fact]
    public void A_rehearsal_or_an_unpaid_order_never_counts()
    {
        IReadOnlyList<Order> orders =
        [
            OrderBuilder.Default().AsTest().WithLines(Line(Mug, "Mug", 9, 10m, "MUGS")).At(Clock).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().WithLines(Line(Kettle, "Kettle", 9, 40m, "KETTLES")).At(Clock).InStatus(OrderStatus.Pending),
        ];

        Assert.Equal(0, ProductPerformanceFigures.Of(DashboardPeriod.Week, PerformanceRanking.Best, null, 10, orders).Total);
    }

    [Fact]
    public void Nothing_sold_is_no_rows_and_the_shops_currency()
    {
        var none = ProductPerformanceFigures.Of(DashboardPeriod.Day, PerformanceRanking.Best, null, 10, []);

        Assert.Equal((0, 0), (none.Total, none.Rows.Count));
    }

    [Fact]
    public void Sixty_products_are_the_limit_and_the_total()
    {
        IReadOnlyList<Order> orders = [Sale([.. Enumerable.Range(1, 60).Select(index => Line(ProductId.New(), $"P{index:00}", index, 1m, "MUGS"))])];

        var best = ProductPerformanceFigures.Of(DashboardPeriod.Week, PerformanceRanking.Best, null, ReadLimits.MaxRows, orders);

        Assert.Equal((60, ReadLimits.MaxRows), (best.Total, best.Rows.Count));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(ReadLimits.MaxRows, true)]
    [InlineData(ReadLimits.MaxRows + 1, false)]
    public void A_limit_is_between_one_and_fifty(int limit, bool valid) =>
        Assert.Equal(valid, new ProductPerformanceValidator()
            .Validate(new GetProductPerformanceQuery(DashboardPeriod.Week, PerformanceRanking.Best, null, limit)).IsValid);
}
