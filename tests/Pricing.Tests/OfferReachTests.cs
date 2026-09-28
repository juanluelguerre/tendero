using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// What counts as "on offer" for the search's filter (R-7.3, F-2): a promotion
/// anybody gets right now, on the line, and what it reaches. Asked at query time
/// and never indexed, because a validity window opens and closes with no event
/// (the facets spec, "Why on offer is not in the index").
/// </summary>
public sealed class OfferReachTests
{
    private static readonly PercentOffLine TwentyOff = new(20);

    private static PromotionCondition InCategory(string category) => new(CategoryCode: category);

    [Fact]
    public void An_automatic_line_promotion_on_a_category_reaches_that_branch()
    {
        var reach = OfferReach.Of([Promotion("SUMMER", TwentyOff, condition: InCategory("APPAREL"))], Now);

        Assert.False(reach.Everything);
        Assert.Equal(["APPAREL"], reach.CategoryCodes);
        Assert.Empty(reach.Skus);
    }

    [Fact]
    public void A_promotion_that_needs_a_coupon_is_not_an_offer_anybody_browsing_gets()
    {
        var reach = OfferReach.Of([Promotion("BF", TwentyOff, condition: InCategory("APPAREL"), coupon: "BF2026")], Now);

        Assert.True(reach.IsNothing);
    }

    [Fact]
    public void A_promotion_for_one_segment_is_not_shown_to_everyone_as_an_offer()
    {
        var reach = OfferReach.Of([Promotion("VIP", TwentyOff, condition: InCategory("APPAREL"), segment: "vip")], Now);

        Assert.True(reach.IsNothing);
    }

    /// <summary>The window is [from, to): Black Friday is on offer from its first second and not at its last.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(47, true)]
    [InlineData(48, false)]
    public void The_validity_window_is_read_at_the_instant_asked(int hoursAfterOpening, bool onOffer)
    {
        var opens = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        var blackFriday = Promotion("BLACKFRIDAY", TwentyOff, condition: InCategory("APPAREL"),
            validFrom: opens, validTo: opens.AddHours(48));

        var reach = OfferReach.Of([blackFriday], opens.AddHours(hoursAfterOpening));

        Assert.Equal(onOffer, !reach.IsNothing);
    }

    public static TheoryData<PromotionEffect, bool> Effects => new()
    {
        { new PercentOffLine(10), true },
        { new BuyXGetY(2, 1), true },
        { new AmountOffOrder(Money(5)), false },
        { new FreeShipping(), false },
    };

    /// <summary>What a line costs is an offer on the product; what the basket costs is not.</summary>
    [Theory]
    [MemberData(nameof(Effects))]
    public void Only_an_effect_on_the_line_puts_a_product_on_offer(PromotionEffect effect, bool onOffer)
    {
        var reach = OfferReach.Of([Promotion("P", effect, condition: InCategory("KITCHEN"))], Now);

        Assert.Equal(onOffer, !reach.IsNothing);
    }

    [Fact]
    public void A_promotion_on_one_sku_reaches_that_sku()
    {
        var reach = OfferReach.Of([Promotion("PAN", TwentyOff, condition: new PromotionCondition(Sku: "PAN-24"))], Now);

        Assert.Equal(["PAN-24"], reach.Skus);
        Assert.Empty(reach.CategoryCodes);
        Assert.False(reach.Everything);
    }

    /// <summary>Both named means both must hold, so the SKU — the narrower — is what it reaches.</summary>
    [Fact]
    public void A_condition_naming_a_category_and_a_sku_reaches_the_sku()
    {
        var reach = OfferReach.Of(
            [Promotion("PAN", TwentyOff, condition: new PromotionCondition(CategoryCode: "KITCHEN", Sku: "PAN-24"))], Now);

        Assert.Equal(["PAN-24"], reach.Skus);
        Assert.Empty(reach.CategoryCodes);
    }

    [Fact]
    public void A_condition_naming_neither_reaches_everything()
    {
        var reach = OfferReach.Of(
            [
                Promotion("SHIRTS", TwentyOff, condition: InCategory("SHIRT")),
                Promotion("ALL", new PercentOffLine(5)),
            ], Now);

        Assert.True(reach.Everything);
    }

    /// <summary>"3 for 2 on shirts" needs three in the cart and is still exactly what somebody filtering by offer wants.</summary>
    [Fact]
    public void A_minimum_quantity_or_subtotal_does_not_stop_it_being_an_offer()
    {
        var reach = OfferReach.Of(
            [
                Promotion("SHIRT3X2", new BuyXGetY(2, 1), condition: new PromotionCondition(CategoryCode: "SHIRT", MinimumQuantity: 3)),
                Promotion("PANS", TwentyOff, condition: new PromotionCondition(MinimumSubtotal: Money(40), CategoryCode: "cookware")),
            ], Now);

        Assert.Equal(["COOKWARE", "SHIRT"], reach.CategoryCodes);
    }

    [Fact]
    public async Task The_port_answers_with_the_same_rule_over_whatever_it_holds()
    {
        IPromotionReader reader = new Promotions(
            Promotion("SUMMER", TwentyOff, condition: InCategory("APPAREL")),
            Promotion("SHIP0", new FreeShipping()));

        var reach = await reader.OffersRunningAtAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(["APPAREL"], reach.CategoryCodes);
        Assert.False(reach.Everything);
    }

    private sealed class Promotions(params Promotion[] all) : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>(all);
    }
}
