namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// What the offers running at one instant reach: the category branches and the
/// SKUs a shopper would see discounted on the line, or everything.
///
/// **It is asked, never stored** (R-7.3, the facets spec). A promotion has a
/// validity window, and nothing raises an event when one opens: a flag written
/// into the search index when a product was projected would be true after Black
/// Friday closed and false before it opened, until something reindexed at the
/// right minute. So the search filters on these values at query time, and the
/// rule of what counts as an offer lives here, beside the promotion, where the
/// engine that applies it can be read in the same file.
/// </summary>
/// <param name="Everything">A running offer whose condition names neither a
/// category nor a SKU: it reaches every line, so every product is on offer.</param>
/// <param name="CategoryCodes">The branches reached, upper case, the way the
/// catalogue writes a category code. A branch reaches everything below it.</param>
/// <param name="Skus">The SKUs reached one by one.</param>
public sealed record OfferReach(bool Everything, IReadOnlyList<string> CategoryCodes, IReadOnlyList<string> Skus)
{
    public static readonly OfferReach Nothing = new(false, [], []);

    public bool IsNothing => !Everything && CategoryCodes.Count == 0 && Skus.Count == 0;

    /// <summary>
    /// **An offer is a promotion a shopper browsing the shop gets on the product
    /// itself, right now.** Each condition removes one way of promising what the
    /// till would not honour:
    ///
    /// - running at <paramref name="at"/> — inside its validity window;
    /// - public — automatic, no coupon to bring, and no customer segment
    ///   (<see cref="Promotion.IsPublicAt"/>, the rule the shop window already
    ///   applies);
    /// - on the line — a percentage off the line, or buy X get Y. An amount off
    ///   the ORDER and free shipping are real promotions, but they are about the
    ///   basket: marking every product "on offer" because shipping is free over
    ///   50 € would make the filter mean nothing.
    ///
    /// A minimum quantity or a minimum subtotal does not disqualify one: "3 for
    /// 2 on shirts" is exactly what a shopper filtering by offer is looking for,
    /// and the cart states its condition. A condition naming both a category and
    /// a SKU is reached through the SKU, the narrower of the two.
    /// </summary>
    public static OfferReach Of(IEnumerable<Promotion> promotions, DateTimeOffset at)
    {
        var categories = new SortedSet<string>(StringComparer.Ordinal);
        var skus = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var promotion in promotions.Where(promotion => promotion.IsPublicAt(at) && IsOnTheLine(promotion.Effect)))
        {
            switch (promotion.Condition)
            {
                case { Sku: { } sku }:
                    skus.Add(sku);
                    break;
                case { CategoryCode: { } category }:
                    categories.Add(category.ToUpperInvariant());
                    break;
                default:
                    return new OfferReach(true, [], []);
            }
        }

        return new OfferReach(false, [.. categories], [.. skus]);
    }

    /// <summary>Two effects change what a line costs; the other two
    /// (<see cref="AmountOffOrder"/>, <see cref="FreeShipping"/>) change what the
    /// basket costs.</summary>
    private static bool IsOnTheLine(PromotionEffect effect) => effect is PercentOffLine or BuyXGetY;
}
