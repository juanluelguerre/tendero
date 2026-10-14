using ElGuerre.Tendero.Pricing.Domain;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// Which promotions a product is in (phase 13c): what its card says beside the
/// price, and what the shopper's assistant may say of it. Asked "muéstrame menaje
/// con esos 5 €", Tino wanted to show a baking dish *because it is in the 5 €
/// promotion*, and nothing said so of any product; and 33 kitchenware cards said
/// nothing of the 5 € either. The till's own rule (<see cref="PromotionCondition.Reaches"/>)
/// says which, so the card and the till cannot disagree.
/// </summary>
public sealed class ProductDealsTests
{
    private const string Pan = "PAN-1";
    private const string KitchenPath = "HOME/KITCHEN/COOKWARE";

    private static readonly Promotion KitchenFive = Promotion(
        "KITCHEN5", new AmountOffOrder(Money(5)), condition: new PromotionCondition(MinimumSubtotal: Money(40), CategoryCode: "KITCHEN"));

    [Fact]
    public void An_order_promotion_on_a_category_is_a_deal_of_every_product_in_its_branch()
    {
        var deals = ProductDeals.Reaching([KitchenFive], Now, Pan, KitchenPath);

        Assert.Equal(["KITCHEN5"], deals.Select(deal => deal.Code));
    }

    [Fact]
    public void A_product_outside_the_branch_has_none()
    {
        Assert.Empty(ProductDeals.Reaching([KitchenFive], Now, "SHIRT-1", "APPAREL/SHIRT"));
    }

    /// <summary>
    /// Free shipping over 50 € reaches every product, so it says nothing about any:
    /// on every card it would be wallpaper. A deal is a promotion that names what it
    /// is about.
    /// </summary>
    [Fact]
    public void A_promotion_about_no_category_or_item_is_no_products_deal()
    {
        var everything = Promotion("SHIP0", new FreeShipping(), condition: new PromotionCondition(MinimumSubtotal: Money(50)));

        Assert.Empty(ProductDeals.Reaching([everything], Now, Pan, KitchenPath));
    }

    [Fact]
    public void Only_what_anybody_gets_right_now_without_a_code()
    {
        var withCode = Promotion("K-CODE", new PercentOffLine(10), condition: new PromotionCondition(CategoryCode: "KITCHEN"), coupon: "K10");
        var forVips = Promotion("K-VIP", new PercentOffLine(10), condition: new PromotionCondition(CategoryCode: "KITCHEN"), segment: "vip");
        var over = Promotion("K-OVER", new PercentOffLine(10), condition: new PromotionCondition(CategoryCode: "KITCHEN"),
            validFrom: Now.AddDays(-7), validTo: Now);

        Assert.Empty(ProductDeals.Reaching([withCode, forVips, over], Now, Pan, KitchenPath));
    }

    [Fact]
    public void One_item_by_its_sku_and_the_shops_order_of_promotions()
    {
        var thisPan = Promotion("PAN-3X2", new BuyXGetY(2, 1), priority: 5, condition: new PromotionCondition(Sku: Pan));

        var deals = ProductDeals.Reaching([KitchenFive, thisPan], Now, Pan, KitchenPath);

        Assert.Equal(["PAN-3X2", "KITCHEN5"], deals.Select(deal => deal.Code));
        Assert.Empty(ProductDeals.Reaching([thisPan], Now, "PAN-2", KitchenPath));
    }
}
