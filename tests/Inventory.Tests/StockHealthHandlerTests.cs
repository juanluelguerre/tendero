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
}
