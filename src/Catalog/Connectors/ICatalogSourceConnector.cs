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

    /// <summary>
    /// What the source as a whole did not hand over cleanly, known once the
    /// stream has been read — a language the shop does not speak, say (phase 16,
    /// P16-5). What concerns one product travels on it, in
    /// <see cref="ExternalProduct.Notes"/>. A source with nothing to say says
    /// nothing, which is the default.
    /// </summary>
    IReadOnlyList<SourceNote> SourceNotes => [];
}

/// <summary>
/// What did not come across cleanly from a source, as a closed set the import's
/// answer names (phase 16, docs/specs/shopify-import.md, *What the import says
/// beside its counts*).
/// </summary>
public enum ImportNoteCode
{
    /// <summary>The source's category has no category here; the product came without one.</summary>
    CategoryNotMapped,

    /// <summary>The source publishes a language the shop does not speak; its texts were not read.</summary>
    LocaleIgnored,

    /// <summary>A description held a table or a list, kept as plain lines.</summary>
    DescriptionHadLayout,

    /// <summary>A variant had no SKU at the source and was given ours.</summary>
    VariantWithoutSku,

    /// <summary>Several variants shared one SKU at the source; each was given ours.</summary>
    SkuSharedAtSource,

    /// <summary>The source renamed a variant's SKU; ours, which stock and orders speak, stays.</summary>
    SkuChangedAtSource,

    /// <summary>A picture could not be read; the product came without it.</summary>
    ImageUnreadable,
}

/// <summary>
/// One note from a source: its code, what it is about (a product's external id,
/// or null for the source as a whole), and the source's own detail — the
/// category's name, the locale, the variant's id.
/// </summary>
public sealed record SourceNote(ImportNoteCode Code, string? About, string? Detail);

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
    IReadOnlyList<ExternalVariant>? Variants = null,

    /// <summary>
    /// The product's name in the source's own URLs — a Shopify handle,
    /// <c>moka-coffee-maker</c> — or null when the source has none. Kept beside
    /// the id, which never changes while this may, so that a shop leaving the
    /// source can redirect its old addresses (phase 16, spec decision 4).
    /// </summary>
    string? Handle = null,

    /// <summary>What the source could not hand over cleanly about this product (phase 16, P16-5).</summary>
    IReadOnlyList<SourceNote>? Notes = null)
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
/// <param name="ExternalId">The source's own id for the variant, when it has one
/// (a Shopify store's do; the seed's do not). A variant is found again by it, so
/// a SKU renamed at the source adds no second variant (phase 16).</param>
/// <param name="Sku">The source's SKU, when it has one. It becomes ours when no
/// sibling shares it (spec decision 1); otherwise ours is the product's id and
/// <paramref name="Key"/>.</param>
/// <param name="Barcode">The source's barcode (EAN, UPC), when it has one.</param>
/// <param name="PriceAmount">What the source sells this variant for, in the
/// product's currency, or null when it prices the product alone.</param>
/// <param name="TaxClass">The tax class the source puts it in (<c>standard</c>,
/// <c>exempt</c>…), or null when it says nothing and the variant keeps its own
/// (phase 16, S2a).</param>
public sealed record ExternalVariant(
    string Key,
    IReadOnlyDictionary<string, string> Attributes,
    string? ExternalId = null,
    string? Sku = null,
    string? Barcode = null,
    decimal? PriceAmount = null,
    string? TaxClass = null);

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
