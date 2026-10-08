using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Catalog's answer to <see cref="IProductPictures"/>: every picture of every
/// product still in the catalogue, by product code and then by its place in the
/// gallery — and, for each, the colour it has to be held to.
///
/// **What a picture is of** (phase 13a, I-4d). Nothing is stored about it: it is
/// read from what the catalogue already holds. A variant's own picture
/// (<see cref="Variant.Image"/>) shows that variant; the cover shows every
/// variant that has no picture of its own; any other picture of the gallery — a
/// detail, the back — shows the product and no variant in particular. A
/// variant's colour is its place on the colour axis, or the product's own colour
/// when it has no such axis, which is every product an import brought with one
/// colour. A picture has a colour to be held to when the variants it shows all
/// hold the same one.
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
                .Select(image => new ProductPicture(product.Id.Value, product.Code, image.Id.Value, ColourShown(product, image)))),
    ];

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

    /// <summary>The one colour the variants a picture shows hold, or null when it shows none in particular or more than one.</summary>
    private static string? ColourShown(Product product, ProductImage picture)
    {
        var isCover = product.PrimaryImage?.Id == picture.Id;
        string?[] colours =
        [
            .. product.Variants
                .Where(variant => variant.Status != VariantStatus.Discontinued)
                .Where(variant => variant.Image == picture.Id || (isCover && variant.Image is null))
                .Select(variant => variant.AxisValues.GetValueOrDefault(Colour) ?? product.AttributeFor(Colour)?.OptionCode)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        return colours is [{ } one] ? one : null;
    }
}
