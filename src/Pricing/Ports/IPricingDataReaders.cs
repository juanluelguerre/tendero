using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>
/// Where the tariffs come from. A committed file today; when the backoffice
/// lets you edit them, a Postgres adapter registers in its place and nothing
/// else changes.
///
/// That second half is deliberately what is NOT built yet. In phase 2 a table of
/// attribute definitions was created and dropped the next day because nothing
/// read or wrote it; doing the same here would be repeating the mistake
/// knowingly.
/// </summary>
public interface IPriceListReader
{
    Task<PriceBook> BookAsync(CancellationToken cancellationToken = default);
}

public interface IPromotionReader
{
    Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// What the offers running at <paramref name="at"/> reach, for the search's
    /// "on offer" filter (<see cref="OfferReach.Of"/>). On the port, so an
    /// adapter that can answer it without reading every promotion — a table with
    /// an index on the window — may, and every caller keeps the same rule.
    /// </summary>
    async Task<OfferReach> OffersRunningAtAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        OfferReach.Of(await AllAsync(cancellationToken), at);
}

/// <summary>
/// What the catalogue knows about a SKU and Pricing needs: its list price, its
/// category branch and its tax class.
///
/// It exists so that Pricing does not reference Catalog. The adapter lives in
/// Persistence, already the only project that knows EF Core exists, so the
/// Catalog-to-Pricing edge is resolved where every other adapter already lives.
/// </summary>
public sealed record PricedItem(
    VariantId VariantId,
    ProductId ProductId,
    string Sku,
    Money CatalogPrice,
    string? CategoryPath,
    string? TaxClass);

public interface IPricedItemReader
{
    /// <summary>
    /// Whichever SKUs exist, in any order. The ones that do not simply do not
    /// come back: the caller compares and reports what is missing, rather than
    /// taking an exception per line.
    /// </summary>
    Task<IReadOnlyList<PricedItem>> BySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);
}
