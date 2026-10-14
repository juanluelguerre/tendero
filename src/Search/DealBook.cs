namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// The promotions running when a page was read, ready to say which a product is in
/// (phase 13c). Pricing's rule decides it (<c>ProductDeals</c>); this only carries
/// the answer to the cards, so Search keeps seeing Pricing through one adapter
/// (<c>PricingRunningOffers</c>) and the readers of the index ask once per page,
/// not once per card.
/// </summary>
public sealed class DealBook(Func<string, string?, IReadOnlyList<ProductDeal>> reaching)
{
    /// <summary>No promotion running, or a reader with no Pricing to ask.</summary>
    public static readonly DealBook None = new((_, _) => []);

    /// <param name="sku">The variant on the card.</param>
    /// <param name="branch">Its category branch, root first: ["HOME", "KITCHEN", "COOKWARE"].</param>
    public IReadOnlyList<ProductDeal> For(string sku, IReadOnlyList<string> branch) =>
        reaching(sku, branch.Count == 0 ? null : string.Join('/', branch));
}
