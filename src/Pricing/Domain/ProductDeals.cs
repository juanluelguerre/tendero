namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// The promotions a product is in (phase 13c): what its card says beside its price
/// and what the shopper's assistant may say of it — "5 € menos en tu pedido desde
/// 40 €" on a pan, which <see cref="OfferReach"/> rightly does not call *on offer*,
/// because the pan's own price does not move.
///
/// A deal is a promotion that:
/// - anybody gets right now, without a code or a segment (<see cref="Promotion.IsPublicAt"/>,
///   the window's and the offer filter's rule);
/// - names what it is about — a category or an item. Free shipping over 50 € reaches
///   every product and so says nothing of any: on every card it would be wallpaper;
/// - reaches this product by the till's own rule (<see cref="PromotionCondition.Reaches"/>),
///   so a card cannot promise what the till would not apply.
///
/// In the order the till weighs them: priority, then code.
/// </summary>
public static class ProductDeals
{
    /// <param name="categoryPath">The product's branch, "HOME/KITCHEN/COOKWARE".</param>
    public static IReadOnlyList<Promotion> Reaching(
        IEnumerable<Promotion> promotions, DateTimeOffset at, string sku, string? categoryPath) =>
    [
        .. promotions
            .Where(promotion => promotion.IsPublicAt(at)
                && (promotion.Condition.CategoryCode is not null || promotion.Condition.Sku is not null)
                && promotion.Condition.Reaches(sku, categoryPath))
            .OrderBy(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Code, StringComparer.Ordinal),
    ];
}
