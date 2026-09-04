using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Connectors;

/// <summary>
/// The catalogue's inbound port. Each source (seed, shopify, medusa, prestashop…)
/// implements this contract and registers as a keyed service under its Source name.
/// The ImportProducts slice knows only this interface, never a concrete source.
/// </summary>
public interface ICatalogSourceConnector
{
    /// <summary>A stable lowercase identifier: "seed", "shopify", "medusa"…</summary>
    string Source { get; }

    /// <summary>
    /// A stream of the source's products. IAsyncEnumerable on purpose: a
    /// catalogue of 150k products must not be loaded whole into memory.
    /// </summary>
    IAsyncEnumerable<ExternalProduct> StreamProductsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The contract's DTO, multilingual from the source: texts arrive as culture ->
/// value dictionaries ("es", "en"). When a source has only one language (an
/// English-only Shopify store, say) it delivers that single key, and the AI
/// enrichment slice fills in the translations that are missing.
/// </summary>
public sealed record ExternalProduct(
    string ExternalId,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string>? Descriptions,
    string? Brand,
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    IReadOnlyList<ExternalImage> Images,
    IReadOnlyDictionary<string, string> Attributes,

    /// <summary>
    /// When the SOURCE first listed it, or null when the source does not say.
    ///
    /// It is a supplier fact and not ours, which is the whole reason it belongs
    /// on this contract: a shop cannot know when a product was designed, and it
    /// should not invent it. What it CAN do is repeat what the feed said, which
    /// is what makes "new arrivals" a claim about the catalogue rather than a
    /// sort by internal id dressed up as one.
    ///
    /// Nullable because a connector may not carry it, and a row without one is
    /// simply never new — not an import failure.
    /// </summary>
    DateOnly? AvailableFrom = null)
{
    public Money Price => new(PriceAmount, PriceCurrency);

    public LocalizedText LocalizedName => new(Names);

    public LocalizedText? LocalizedDescription =>
        Descriptions is { Count: > 0 } ? new LocalizedText(Descriptions) : null;
}

/// <summary>
/// An image as the source offers it. <see cref="Location"/> is a Uri on purpose:
/// it covers <c>https</c> (Shopify serves from its CDN) and <c>file</c> (the seed
/// connector reads from disk, and phase 4's PDF scanning will write to a temp
/// file). One type for both cases, with no hierarchy.
///
/// Importing does NOT keep this reference: it downloads the content and stores
/// it in our own store. The source may delete its copy whenever it likes (see
/// docs/adr/0011-product-images.md).
/// </summary>
public sealed record ExternalImage(
    Uri Location,
    IReadOnlyDictionary<string, string>? Alt = null)
{
    public LocalizedText? LocalizedAlt =>
        Alt is { Count: > 0 } ? new LocalizedText(Alt) : null;

    public bool IsAbsoluteUri() => Location.IsAbsoluteUri;
}
