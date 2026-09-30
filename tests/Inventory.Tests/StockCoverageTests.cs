using ElGuerre.Tendero.Inventory.Features.StockHealth;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// At the pace the shop sells, how many days until a SKU runs out (R-9, D-7;
/// the spec's *Definitions*): units available ÷ units sold per day, the pace
/// being the last 28 days ÷ 28 — or the days on sale, when fewer.
/// </summary>
public sealed class StockCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_backpack_with_18_available_and_42_sold_in_28_days_covers_12_days()
    {
        var pace = StockCoverage.PerDay(42, onSaleSince: Now.AddDays(-400), Now);

        Assert.Equal((1.5m, 12m), (pace, StockCoverage.Days(18, pace!.Value)));
    }

    /// <summary>Nothing sold in 28 days is a signal of its own — not a coverage that never ends.</summary>
    [Fact]
    public void A_sku_that_sold_nothing_in_28_days_has_no_sales_rather_than_an_endless_coverage() =>
        Assert.Null(StockCoverage.PerDay(0, onSaleSince: Now.AddDays(-400), Now));

    [Fact]
    public void A_sku_on_sale_for_ten_days_divides_by_ten() =>
        Assert.Equal(2m, StockCoverage.PerDay(20, onSaleSince: Now.AddDays(-10), Now));

    [Fact]
    public void A_sku_on_sale_since_this_morning_divides_by_one_day_not_by_nothing() =>
        Assert.Equal(3m, StockCoverage.PerDay(3, onSaleSince: Now.AddHours(-4), Now));

    [Fact]
    public void Sold_out_covers_nothing() => Assert.Equal(0m, StockCoverage.Days(0, 1.5m));

    [Fact]
    public void Sell_through_is_sold_over_sold_and_left()
    {
        Assert.Equal(0.75m, StockCoverage.SellThrough(sold: 30, left: 10));
        Assert.Null(StockCoverage.SellThrough(sold: 0, left: 0));
    }
}
