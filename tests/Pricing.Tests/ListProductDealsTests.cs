using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ListProductDeals;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// The promotions one SKU is in, for the product's page (phase 13c's follow-ups).
/// The card already says it, from the search; the page reads the catalogue, which
/// may not ask Pricing, so it asks here. The same rule as the card's and the till's
/// (<see cref="ProductDeals"/>), over the branch the till uses.
/// </summary>
public sealed class ListProductDealsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Promotion KitchenFive = new(
        "KITCHEN5", Text("5 € en menaje", "5 € off cookware"),
        new PromotionCondition(MinimumSubtotal: Money(40), CategoryCode: "KITCHEN"),
        new AmountOffOrder(Money(5)), CombinationPolicy.Stackable, 30);

    private sealed class Catalogue : IPricedItemReader
    {
        public Task<IReadOnlyList<PricedItem>> BySkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PricedItem>>(
                skus.Contains("PAN-1", StringComparer.OrdinalIgnoreCase)
                    ? [new PricedItem(VariantId.New(), ProductId.New(), "PAN-1", Money(44.95m), "HOME/KITCHEN/COOKWARE", null)]
                    : []);
    }

    private static ListProductDealsHandler Handler() =>
        new(new Catalogue(), new GivenPromotions(KitchenFive), new ElGuerre.Tendero.Tests.TestClock(Now));

    [Theory]
    [InlineData("es", "5 € en menaje")]
    [InlineData("en", "5 € off cookware")]
    public async Task A_sku_in_a_promotions_branch_is_in_it_named_in_the_readers_language(string culture, string name)
    {
        var result = await Handler().HandleAsync(new ListProductDealsQuery("PAN-1", culture), Ct);

        var deal = Assert.Single(result.Items);
        Assert.Equal(("KITCHEN5", name, KitchenFive.Terms), (deal.Code, deal.Name, deal.Terms));
    }

    /// <summary>A SKU the shop does not sell — unknown, or a draft — is in no promotion: an answer, not an error.</summary>
    [Fact]
    public async Task A_sku_the_shop_does_not_sell_is_in_none()
    {
        Assert.Empty((await Handler().HandleAsync(new ListProductDealsQuery("NOPE", "es"), Ct)).Items);
    }
}
