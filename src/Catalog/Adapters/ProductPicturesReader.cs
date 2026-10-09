using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Catalog's answer to <see cref="IProductPictures"/>: every picture of every
/// product still in the catalogue, by product code and then by its place in the
/// gallery — and, for each, the colour it has to be held to.
///
/// **What a picture is of** (phase 13a, I-4d): what the picture says it shows
/// (<see cref="ProductImage.Shows"/>). A picture that shows a colour is held to
/// that colour. One that says nothing shows the product and no variant in
/// particular — the back, a detail — and is held to none; except the cover of a
/// product that does not vary by colour and says its own, which is every product
/// an import brought in one colour: that cover is the product, in its colour.
///
/// In memory over the live catalogue: a hundred products (the scale rule's
/// "deferred with its number"); the full catalogue makes it SQL.
/// </summary>
public sealed class ProductPicturesReader(IProductCatalogReader products, IAttributeDefinitionReader definitions) : IProductPictures
{
    /// <summary>The attribute a product's colour is: an axis of its variants, or a value of its own.</summary>
    public const string Colour = "COLOR";

    public async Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default) =>
    [
        .. (await products.LiveAsync(cancellationToken))
            .OrderBy(product => product.Code, StringComparer.Ordinal)
            .SelectMany(product => product.Images
                .OrderBy(image => image.SortOrder)
                .Select(image => Picture(product, image))),
    ];

    private static ProductPicture Picture(Product product, ProductImage image)
    {
        var colour = ColourShown(product, image);

        // What the product's variants hold besides this picture's colour: only
        // asked of a picture that has a colour to be held to.
        string[] others = colour is null
            ? []
            :
            [
                .. product.Variants
                    .Where(variant => variant.Status != VariantStatus.Discontinued)
                    .Select(variant => variant.AxisValues.GetValueOrDefault(Colour))
                    .OfType<string>()
                    .Where(other => !string.Equals(other, colour, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];

        return new ProductPicture(product.Id.Value, product.Code, image.Id.Value, colour, others);
    }

    public async Task<IReadOnlyList<PictureColour>> ColoursAsync(CancellationToken cancellationToken = default)
    {
        var options = (await definitions.AllAsync(cancellationToken)).ByCode(Colour)?.Options ?? [];

        // Declared on one side, true of both: grey may be taken for stainless steel too.
        return
        [
            .. options.Select(option => new PictureColour(
                option.Code,
                option.Label.In("es"),
                option.Label.In("en"),
                [
                    .. (option.MistakenFor ?? [])
                        .Concat(options.Where(other => other.MistakenFor?.Contains(option.Code) == true).Select(other => other.Code))
                        .Distinct(StringComparer.Ordinal),
                ])),
        ];
    }

    /// <summary>The colour a picture is held to: the one it says it shows, or the product's own for the cover of a product of one colour.</summary>
    private static string? ColourShown(Product product, ProductImage picture)
    {
        if (picture.Shows.FirstOrDefault(shown => string.Equals(shown.Axis, Colour, StringComparison.OrdinalIgnoreCase)) is { } shown)
            return shown.Value;

        return !product.VariantAxes.Contains(Colour, StringComparer.OrdinalIgnoreCase) && product.PrimaryImage?.Id == picture.Id
            ? product.AttributeFor(Colour)?.OptionCode
            : null;
    }
}
