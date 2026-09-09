using ElGuerre.Tendero.Catalog.Features.GetProduct;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// Tendero's product, said in UCP's words.
///
/// **It maps from <see cref="ProductDetail"/> and not from a search hit**, and
/// that is a decision rather than convenience. UCP requires a description on
/// every product and on every variant; a search hit does not carry one, because
/// the index holds what RANKING needs and the page holds what a shopper decides
/// on. Rather than widen the shop's own search contract to satisfy somebody
/// else's protocol, the binding looks each result up — bounded by the
/// specification's own default page size of ten.
///
/// That is an N+1 and it is recorded as one: ten lookups per search, against a
/// hundred products. It enters the standing backlog with that number, exactly
/// like outbox coalescing and bulk indexing, and it is fixed by projecting a
/// description onto the search document the day the number is worth fixing.
/// </summary>
public static class UcpCatalogMapper
{
    /// <summary>
    /// Money in ISO 4217 minor units.
    ///
    /// Two decimal places, which is right for the euro this shop sells in and
    /// wrong for the yen (zero) and the dinar (three). It is not a table because
    /// the shop is single-currency and enforces it — a second currency is
    /// already a backlog row, and it brings this with it. Written as a named
    /// method rather than inline so the day that happens there is one place to
    /// change and one place to look.
    /// </summary>
    public static UcpPrice Money(decimal amount, string currency) => new()
    {
        Amount = (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven),
        Currency = currency
    };

    public static UcpProduct From(ProductDetail product, string baseUrl) => new()
    {
        // The public code, not the internal id: it is what an agent can hold on
        // to, which is the whole argument of ADR 0026.
        Id = product.Code,
        Handle = product.Slug,
        Title = product.Name,
        Description = new UcpDescription { Text = product.Description ?? string.Empty },
        Categories = product.Category.Count == 0
            ? null
            : [.. product.Category.Select(step => new UcpCategory { Id = step.Code, Name = step.Name })],
        PriceRange = new UcpPriceRange
        {
            Min = Money(product.PriceFrom, product.PriceCurrency),
            Max = Money(product.PriceTo, product.PriceCurrency)
        },
        Media = Media(product.Images, baseUrl),
        Options = product.Axes.Count == 0
            ? null
            : [.. product.Axes.Select(axis => new UcpProductOption
            {
                Name = axis.Label,
                Values = [.. axis.Options.Select(option => new UcpOptionValue
                {
                    Id = option.Code,
                    Label = option.Label
                })]
            })],
        Variants = [.. product.Variants.Select(variant => Variant(variant, product, baseUrl))],
        Tags = product.Brand is null ? null : [product.Brand]
    };

    private static UcpVariant Variant(VariantView variant, ProductDetail product, string baseUrl) => new()
    {
        Id = variant.Sku,
        Sku = variant.Sku,

        // "Blue / Large" in the specification's own example, which is what the
        // product page already computes for its picker.
        Title = variant.Label.Length == 0 ? product.Name : variant.Label,

        // The variant inherits the product's words. Tendero does not write
        // per-variant copy, and inventing some here to fill a required field
        // would be the binding making things up.
        Description = new UcpDescription { Text = product.Description ?? string.Empty },
        Price = Money(variant.PriceAmount, variant.PriceCurrency),

        // Per variant, which is only possible because the variant is the indexed
        // unit (ADR 0015). A product-level flag would say "some size is
        // available" and let an agent buy the one that is not.
        Availability = new UcpAvailability { InStock = variant.InStock },
        Options = variant.AxisValues.Count == 0
            ? null
            : [.. variant.AxisValues.Select(pair => new UcpSelectedOption
            {
                Name = LabelOfAxis(product, pair.Key),
                Id = pair.Value,
                Label = LabelOfOption(product, pair.Key, pair.Value)
            })],
        Media = variant.ImageId is null
            ? null
            : [new UcpMedia { Url = ImageUrl(variant.ImageId, baseUrl), Type = "image" }]
    };

    private static IReadOnlyList<UcpMedia>? Media(IReadOnlyList<ProductImageView> images, string baseUrl) =>
        images.Count == 0
            ? null
            : [.. images
                .OrderBy(image => image.SortOrder)
                .Select(image => new UcpMedia
                {
                    Url = ImageUrl(image.ImageId, baseUrl),
                    Type = "image",
                    Alt = image.Alt
                })];

    private static string ImageUrl(string imageId, string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/api/images/{imageId}";

    /// <summary>
    /// The axis's human label, falling back to its code. A shop that had not
    /// defined the axis would otherwise send an agent a bare
    /// <c>COLOR</c> — which is the exact defect the localized taxonomy fixed for
    /// search in phase 2.
    /// </summary>
    private static string LabelOfAxis(ProductDetail product, string code) =>
        product.Axes.FirstOrDefault(axis =>
            string.Equals(axis.Code, code, StringComparison.OrdinalIgnoreCase))?.Label ?? code;

    private static string LabelOfOption(ProductDetail product, string axisCode, string optionCode) =>
        product.Axes
            .FirstOrDefault(axis => string.Equals(axis.Code, axisCode, StringComparison.OrdinalIgnoreCase))?
            .Options
            .FirstOrDefault(option => string.Equals(option.Code, optionCode, StringComparison.OrdinalIgnoreCase))?
            .Label
        ?? optionCode;
}
