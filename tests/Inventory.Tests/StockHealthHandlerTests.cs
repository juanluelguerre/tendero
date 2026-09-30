using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// Inventory owns the stock and Ordering the pace (R-9, D-7): the handler composes
/// its shelves with the units sold it reads through <see cref="ISalesPace"/>.
/// </summary>
public sealed class StockHealthHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedPace(Dictionary<string, int> lastFourWeeks, Dictionary<string, int> period) : ISalesPace
    {
        public Task<IReadOnlyDictionary<string, SkuPace>> SoldAsync(
            IReadOnlyCollection<string> skus, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            var source = to - from == TimeSpan.FromDays(28) ? lastFourWeeks : period;
            return Task.FromResult<IReadOnlyDictionary<string, SkuPace>>(skus.ToDictionary(
                sku => sku, sku => new SkuPace(source.GetValueOrDefault(sku), Now.AddDays(-400))));
        }
    }

    [Fact]
    public async Task It_counts_what_runs_out_soon_what_does_not_sell_and_what_moves()
    {
        var stock = new FakeStock();
        stock.Put("BACKPACK", "MAD", 18);   // 42 sold in 28 days: 12 days
        stock.Put("LAMP", "MAD", 60);       // 28 sold: 60 days
        stock.Put("MUG", "MAD", 5);         // nothing sold: no sales
        stock.Put("PAN", "MAD", 0);         // sold out
        var pace = new FixedPace(
            new() { ["BACKPACK"] = 42, ["LAMP"] = 28, ["PAN"] = 10 },
            new() { ["BACKPACK"] = 6, ["LAMP"] = 2, ["PAN"] = 3 });
        var handler = new StockHealthHandler(stock, pace, new TestClock(Now));

        var health = await handler.HandleAsync(new GetStockHealthQuery(DashboardPeriod.Week), TestContext.Current.CancellationToken);

        Assert.Equal((1, 1, 1), (health.UnderTwoWeeks, health.NoSales, health.SoldOut));
        Assert.Equal(new SkuCoverage("BACKPACK", 18, 1.5m, 12m), health.Closest[0]);
        Assert.Equal("PAN", health.Fastest[0].Sku);   // 3 sold, none left: 100 %
        Assert.Equal("MUG", health.Slowest[0].Sku);   // none sold, 5 left: 0 %
    }

    private static Task<StockHealth> Read(FakeStock stock, Dictionary<string, int> lastFourWeeks, Dictionary<string, int>? period = null) =>
        new StockHealthHandler(stock, new FixedPace(lastFourWeeks, period ?? lastFourWeeks), new TestClock(Now))
            .HandleAsync(new GetStockHealthQuery(DashboardPeriod.Week), TestContext.Current.CancellationToken);

    /// <summary>
    /// Each warehouse on its own, in code order, over what it has on its shelves and
    /// sells: MAD's 30 and 10 days meet at 20; BCN's sold-out SKU covers nothing and
    /// is not a zero in its median. At 28 sold in 28 days, a unit is a day.
    /// </summary>
    [Fact]
    public async Task A_warehouse_s_median_is_over_what_it_has_and_an_even_count_meets_in_the_middle()
    {
        var stock = new FakeStock();
        stock.Put("A", "MAD", 30);
        stock.Put("B", "MAD", 10);
        stock.Put("C", "BCN", 7);
        stock.Put("D", "BCN", 0);

        var health = await Read(stock, new() { ["A"] = 28, ["B"] = 28, ["C"] = 28, ["D"] = 28 });

        Assert.Equal([new WarehouseCoverage("BCN", 7m), new WarehouseCoverage("MAD", 20m)], health.Warehouses);
    }

    [Fact]
    public async Task A_sku_in_two_warehouses_covers_what_both_hold()
    {
        var stock = new FakeStock();
        stock.Put("LAMP", "MAD", 3);
        stock.Put("LAMP", "BCN", 4);

        var health = await Read(stock, new() { ["LAMP"] = 28 });

        Assert.Equal(new SkuCoverage("LAMP", 7, 1m, 7m), Assert.Single(health.Closest));
    }

    /// <summary>
    /// Under two weeks is under fourteen days, not fourteen; and a SKU sold out with
    /// no sales is sold out — *no sales* is stock that sits.
    /// </summary>
    [Fact]
    public async Task Fourteen_days_is_not_under_two_weeks_and_a_sold_out_sku_is_not_one_that_sits()
    {
        var stock = new FakeStock();
        stock.Put("EXACTLY", "MAD", 14);
        stock.Put("SOONER", "MAD", 13);
        stock.Put("GONE", "MAD", 0);

        var health = await Read(stock, new() { ["EXACTLY"] = 28, ["SOONER"] = 28 });

        Assert.Equal((1, 0), (health.UnderTwoWeeks, health.NoSales));
    }

    /// <summary>
    /// What moves is what sold, and what sits is what is left: a SKU that sold
    /// nothing is not among the fastest, one sold out is not among the slowest, and
    /// a tie is broken by the SKU.
    /// </summary>
    [Fact]
    public async Task The_lists_hold_only_what_they_are_about_and_break_ties_by_sku()
    {
        var stock = new FakeStock();
        stock.Put("ZETA", "MAD", 10);
        stock.Put("ALFA", "MAD", 10);
        stock.Put("IDLE", "MAD", 5);
        stock.Put("GONE", "MAD", 0);

        var health = await Read(stock, new() { ["ZETA"] = 28, ["ALFA"] = 28 }, period: new() { ["ZETA"] = 10, ["ALFA"] = 10, ["GONE"] = 4 });

        Assert.Equal(["ALFA", "ZETA"], health.Closest.Select(row => row.Sku));
        Assert.Equal(["GONE", "ALFA", "ZETA"], health.Fastest.Select(row => row.Sku));
        Assert.Equal(["IDLE", "ALFA", "ZETA"], health.Slowest.Select(row => row.Sku));
    }
}
