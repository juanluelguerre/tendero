using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The promotions a card is in (phase 13c): asked of Pricing when a search runs,
/// like "on offer", never indexed — a window opens and closes with no event. A
/// search for "menaje" drew 33 kitchenware cards and none said a word of the 5 €
/// off the order they were all in.
/// </summary>
public sealed class ProductDealsOnCardsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Promotion KitchenFive = new(
        "KITCHEN5", new LocalizedText(new Dictionary<string, string> { ["es"] = "5 € en menaje", ["en"] = "5 € off cookware" }),
        new PromotionCondition(MinimumSubtotal: new Money(40, "EUR"), CategoryCode: "KITCHEN"),
        new AmountOffOrder(new Money(5, "EUR")), CombinationPolicy.Stackable, 30);

    private static ProductSearchDocument Pan(params string[] branch) => new()
    {
        Id = "v-1",
        ProductId = "p-1",
        Sku = "PAN-1",
        Culture = "es",
        Name = "Sartén",
        Code = "PAN1",
        Slug = "sarten",
        PriceCurrency = "EUR",
        Status = "active",
        CategoryCodes = branch,
    };

    [Fact]
    public async Task A_card_in_the_promotions_branch_carries_it_in_the_readers_language_with_its_terms()
    {
        var deals = await new PricingRunningOffers(new TestClock(), new Given(KitchenFive)).DealsNowAsync("es", Ct);

        var deal = Assert.Single(Pan("HOME", "KITCHEN", "COOKWARE").ToHit(1d, deals).Deals);

        Assert.Equal(("KITCHEN5", "5 € en menaje"), (deal.Code, deal.Name));
        Assert.Equal(KitchenFive.Terms, deal.Terms);
    }

    [Fact]
    public async Task A_card_elsewhere_or_read_without_pricing_carries_none()
    {
        var deals = await new PricingRunningOffers(new TestClock(), new Given(KitchenFive)).DealsNowAsync("es", Ct);

        Assert.Empty(Pan("APPAREL", "SHIRT").ToHit(1d, deals).Deals);
        Assert.Empty(Pan("HOME", "KITCHEN").ToHit(1d, await new PricingRunningOffers(new TestClock()).DealsNowAsync("es", Ct)).Deals);
        Assert.Empty(Pan("HOME", "KITCHEN").ToHit(1d).Deals);
    }

    private sealed class Given(params Promotion[] all) : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>(all);
    }
}
