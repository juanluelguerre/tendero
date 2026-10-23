namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// A product as the store says it, before it is ours: what one bulk file holds
/// for it, its variants and pictures gathered under it. Ids are the numbers at
/// the end of Shopify's <c>gid://shopify/Product/8001</c>, which never change
/// while the handle may. Mapping it to <see cref="ExternalProduct"/> is the
/// adapter's job (P16-4), not this record's.
/// </summary>
internal sealed record ShopifyProduct(
    string Id,
    string Handle,
    string Title,
    string? DescriptionHtml,
    string? Vendor,
    DateTimeOffset? PublishedAt,
    ShopifyCategory? Category,
    IReadOnlyDictionary<string, ShopifyTexts> Translations,
    IReadOnlyList<ShopifyVariant> Variants,
    IReadOnlyList<ShopifyImage> Images,
    IReadOnlyDictionary<string, string> Metafields);

/// <summary>A category of Shopify's Standard Product Taxonomy.</summary>
internal sealed record ShopifyCategory(string Id, string FullName);

/// <summary>A product's texts in one of the store's other languages.</summary>
internal sealed record ShopifyTexts(string? Title, string? DescriptionHtml);

/// <summary>
/// One way of buying it. A product sold one way still has one, which Shopify
/// calls "Default Title". An empty SKU at the source is no SKU.
/// </summary>
internal sealed record ShopifyVariant(
    string Id,
    string? Sku,
    string? Barcode,
    decimal Price,
    IReadOnlyList<ShopifyOption> Options,
    string? ImageUrl,
    bool Taxable = true);

internal sealed record ShopifyOption(string Name, string Value);

internal sealed record ShopifyImage(string Url, string? AltText);
