using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The pace Inventory reads through its port, answered from the orders (R-9, D-7):
/// the units a paid, live order sold of each SKU, a zero for one that sold nothing,
/// and since when each has been on sale.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesPaceTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task It_counts_what_sold_of_each_sku_and_a_zero_for_what_did_not()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(It_counts_what_sold_of_each_sku_and_a_zero_for_what_did_not));

        await shop.StockAsync(Sku, 5, ct);
        var cart = await shop.AddToCartAsync(null, Sku, 2, ct);
        var chosen = (await shop.ShippingOptionsAsync(cart.Token, ct)).Options[0];
        var quote = await shop.QuoteAsync(cart.Token, chosen.Amount, ct);
        Assert.Equal(PlaceOrderOutcome.Placed, (await shop.PlaceOrderAsync(cart.Token, chosen.Code, quote.InputHash, ct)).Outcome);

        var sold = await shop.ReadAsync<ISalesPace, IReadOnlyDictionary<string, SkuPace>>(pace =>
            pace.SoldAsync([Sku, "NOT-SOLD"], DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(1), ct));

        Assert.Equal((2, 0, true), (sold[Sku].Units, sold["NOT-SOLD"].Units, sold[Sku].OnSaleSince is not null));
    }
}
