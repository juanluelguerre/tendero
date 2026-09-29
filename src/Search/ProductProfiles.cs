namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// A product as the index knows it, whole: the card a row draws, its leaf
/// category, and the attributes it can be compared on.
///
/// It exists for the home's rows (R-8), which start from products rather than
/// from words — the trail's codes, the best sellers' ids, the viewed products
/// whose neighbours *Puede que te guste* looks for. A search hit is one variant
/// that matched; a profile is the product, every variant read, so that "sold out"
/// means no variant can be bought and "the same colour" means any of its colours.
/// </summary>
/// <param name="Card">
/// The variant a person browsing would be shown — one that can be bought, the
/// cheapest — which is the choice <see cref="ISearchHitReader"/> makes for a card
/// no words chose. Its <see cref="SearchHit.InStock"/> is therefore true when any
/// variant can be bought.
/// </param>
/// <param name="Category">The leaf category's code; null for a product filed nowhere.</param>
/// <param name="Traits">
/// Every "CODE:VALUE" the product carries — its facet attributes and every value
/// of its variant axes ("MATERIAL:ALUMINIO", "COLOR:NAVY", "SIZE:42") — ordered
/// and distinct. Codes, never labels, so a comparison means the same in both
/// cultures' indexes.
/// </param>
public sealed record ProductProfile(SearchHit Card, string? Category, IReadOnlyList<string> Traits)
{
    public string ProductId => Card.ProductId;

    public string Code => Card.Code;

    /// <summary>Sold out is no variant that can be bought.</summary>
    public bool InStock => Card.InStock;

    /// <summary>
    /// The profiles of the products these variant documents belong to, one per
    /// product, in the order each product first appears. The card is the variant
    /// that can be bought, then the cheapest, then the lowest id — so the same
    /// documents always draw the same card.
    /// </summary>
    public static IReadOnlyList<ProductProfile> Of(IEnumerable<ProductSearchDocument> variants) =>
    [
        .. variants
            .GroupBy(variant => variant.ProductId, StringComparer.Ordinal)
            .Select(product =>
            {
                var card = product
                    .OrderByDescending(variant => variant.InStock)
                    .ThenBy(variant => variant.PriceAmount)
                    .ThenBy(variant => variant.Id, StringComparer.Ordinal)
                    .First();

                return new ProductProfile(
                    card.ToHit(score: 0d),
                    card.Category,
                    [
                        .. product.SelectMany(variant => variant.FacetValues.Concat(variant.AxisValues))
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                    ]);
            })
    ];
}

/// <summary>
/// Products read whole from the index, by what the home's rows hold: ids, codes
/// or the categories to look for neighbours in. Only Active products — the index
/// holds nothing else — and never an error for a key nobody owns: it is left out.
/// </summary>
public interface IProductProfileReader
{
    Task<IReadOnlyList<ProductProfile>> ByIdsAsync(IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default);

    /// <summary>By public code, in the canonical form the catalogue writes it (upper case).</summary>
    Task<IReadOnlyList<ProductProfile>> ByCodesAsync(IReadOnlyCollection<string> codes, string culture, CancellationToken ct = default);

    /// <summary>Every product whose LEAF category is one of <paramref name="categories"/> — a neighbour, not the branch.</summary>
    Task<IReadOnlyList<ProductProfile>> InCategoriesAsync(IReadOnlyCollection<string> categories, string culture, CancellationToken ct = default);
}
