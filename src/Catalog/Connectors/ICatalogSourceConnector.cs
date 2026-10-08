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
    DateOnly? AvailableFrom = null,

    /// <summary>
    /// The ways the source sells it, when it tells them apart — a colour, a
    /// size — or null when it draws no such distinction and the product is
    /// bought one way (ADR 0015: it then gets its default variant).
    /// </summary>
    IReadOnlyList<ExternalVariant>? Variants = null)
{
    public Money Price => new(PriceAmount, PriceCurrency);

    public LocalizedText LocalizedName => new(Names);

    public LocalizedText? LocalizedDescription =>
        Descriptions is { Count: > 0 } ? new LocalizedText(Descriptions) : null;
}

/// <summary>
/// One way of buying a product, as the source tells it apart (phase 13a, I-4d).
/// </summary>
/// <param name="Key">What tells it apart in the source's own SKU: <c>OLIVE</c>,
/// <c>BLACK-38</c>. Ours is the product's external id and this, so it is the same
/// on every import.</param>
/// <param name="Attributes">What it varies by, in the source's words — the same
/// keys and values a product's attributes arrive in (<c>color: negro</c>), and
/// resolved against the same definitions.</param>
public sealed record ExternalVariant(string Key, IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// An image as the source offers it. <see cref="Location"/> is a Uri on purpose:
/// it covers <c>https</c> (Shopify serves from its CDN) and <c>file</c> (the seed
/// connector reads from disk, and a later PDF ingestion step will write to a
/// temp file). One type for both cases, with no hierarchy.
///
/// Importing does NOT keep this reference: it downloads the content and stores
/// it in our own store. The source may delete its copy whenever it likes (see
/// docs/adr/0011-product-images.md).
/// </summary>
/// <param name="Shows">
/// Which of the product's variants the picture shows, in the source's words —
/// <c>color: negro</c> — or null when it shows the product and no variant in
/// particular. **The picture says it, not the variant**
/// (`docs/analysis/catalogue-model.md`): a colour may then have several, and
/// every size of the colour has them.
/// </param>
public sealed record ExternalImage(
    Uri Location,
    IReadOnlyDictionary<string, string>? Alt = null,
    IReadOnlyDictionary<string, string>? Shows = null)
{
    public LocalizedText? LocalizedAlt =>
        Alt is { Count: > 0 } ? new LocalizedText(Alt) : null;

    public bool IsAbsoluteUri() => Location.IsAbsoluteUri;
}
