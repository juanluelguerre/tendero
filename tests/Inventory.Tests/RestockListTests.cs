using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// Every SKU to restock with its days of cover (S-3a) — the R-7.1 rule decides who
/// is on the list, the dashboard's coverage says for how long it lasts. Summed
/// across warehouses, as the stock health counts it, unless one is asked for.
/// </summary>
public sealed class RestockListTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Sold in the last four weeks, everything on sale long before.</summary>
    private sealed class FourWeeksPace(Dictionary<string, int> sold) : ISalesPace
    {
        public Task<IReadOnlyDictionary<string, SkuPace>> SoldAsync(
            IReadOnlyCollection<string> skus, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, SkuPace>>(skus.ToDictionary(
                sku => sku, sku => new SkuPace(sold.GetValueOrDefault(sku), Now.AddDays(-400))));
    }

    private static Task<RestockList> List(FakeStock stock, Dictionary<string, int> sold, string? warehouse = null, int limit = ReadLimits.MaxRows) =>
        new RestockListHandler(stock, new FourWeeksPace(sold), new TestClock(Now))
            .HandleAsync(new GetRestockListQuery(warehouse, limit), TestContext.Current.CancellationToken);

    [Fact]
    public async Task At_or_below_the_restock_point_is_listed_and_above_is_not()
    {
        var stock = new FakeStock();
        stock.Put("LOW", "MAD", Restocking.At);
        stock.Put("FINE", "MAD", Restocking.At + 1);

        var list = await List(stock, []);

        Assert.Equal(["LOW"], list.Rows.Select(row => row.Sku));
        Assert.Equal(Restocking.At, list.RestockAt);
    }

    /// <summary>The need is the shop's (R-7.1's point is summed over the warehouses),
    /// and a warehouse asked for narrows the shop's list to what it stocks — never a
    /// warehouse's units against the shop's pace (final review, S-3a).</summary>
    [Fact]
    public async Task Two_warehouses_are_summed_and_a_warehouse_narrows_the_shops_list()
    {
        var stock = new FakeStock();
        stock.Put("MUG", "MAD", 3);
        stock.Put("MUG", "BCN", 4);   // 7 in all: above the point, whatever MAD holds
        stock.Put("CUP", "MAD", 1);
        stock.Put("CUP", "BCN", 2);   // 3 in all: to restock
        stock.Put("PAN", "BCN", 1);   // to restock, but not stocked in MAD

        Assert.Equal(["CUP", "PAN"], (await List(stock, [])).Rows.Select(row => row.Sku).Order());

        var mad = await List(stock, [], warehouse: "mad");
        var cup = Assert.Single(mad.Rows);
        Assert.Equal(("CUP", 3, (int?)1), (cup.Sku, cup.Available, cup.InWarehouse));
        Assert.Null((await List(stock, [])).Rows.First().InWarehouse);
    }

    /// <summary>A warehouse the shop does not have is said to be unknown, with the
    /// ones it has — never an empty list that reads as "nothing to restock".</summary>
    [Fact]
    public async Task A_warehouse_the_shop_does_not_have_is_unknown_and_the_known_ones_are_named()
    {
        var stock = new FakeStock();
        stock.Put("CUP", "MAD", 1);
        stock.Put("CUP", "BCN", 1);

        var list = await List(stock, [], warehouse: "Madrid");

        Assert.True(list.UnknownWarehouse);
        Assert.Equal(["BCN", "MAD"], list.Warehouses);
        Assert.Empty(list.Rows);
    }

    [Fact]
    public async Task What_sold_has_its_cover_and_what_did_not_has_none()
    {
        var stock = new FakeStock();
        stock.Put("SELLS", "MAD", 4);   // 28 sold in 28 days: 1 a day, 4 days
        stock.Put("STILL", "MAD", 4);

        var list = await List(stock, new() { ["SELLS"] = 28 });

        var sells = list.Rows.Single(row => row.Sku == "SELLS");
        Assert.Equal((1m, 4m), (sells.PerDay, sells.CoverageDays));
        var still = list.Rows.Single(row => row.Sku == "STILL");
        Assert.Equal(((decimal?)null, (decimal?)null), (still.PerDay, still.CoverageDays));
    }

    [Fact]
    public async Task Sold_out_first_then_by_days_of_cover_then_what_does_not_sell()
    {
        var stock = new FakeStock();
        stock.Put("SLOW", "MAD", 5);    // 14 sold: 0.5 a day, 10 days
        stock.Put("FAST", "MAD", 5);    // 56 sold: 2 a day, 2.5 days
        stock.Put("NONE", "MAD", 2);
        stock.Put("GONE", "MAD", 0);

        var list = await List(stock, new() { ["SLOW"] = 14, ["FAST"] = 56, ["GONE"] = 10 });

        Assert.Equal(["GONE", "FAST", "SLOW", "NONE"], list.Rows.Select(row => row.Sku));
    }

    [Fact]
    public async Task Sixty_to_restock_are_fifty_rows_and_a_total_of_sixty()
    {
        var stock = new FakeStock();
        foreach (var index in Enumerable.Range(1, 60))
            stock.Put($"SKU-{index:00}", "MAD", 1);

        var list = await List(stock, []);

        Assert.Equal((60, ReadLimits.MaxRows), (list.Total, list.Rows.Count));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(ReadLimits.MaxRows, true)]
    [InlineData(ReadLimits.MaxRows + 1, false)]
    public void A_limit_is_between_one_and_fifty(int limit, bool valid) =>
        Assert.Equal(valid, new RestockListValidator().Validate(new GetRestockListQuery(null, limit)).IsValid);
}
