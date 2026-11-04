using System.Globalization;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Catalog.Rendering;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Catalog's answer to <see cref="ICatalogueExport"/>: the live catalogue rendered
/// as the product page renders it — an option by its label, an attribute the
/// catalogue never defined left out, a variant's gallery as the aggregate derives
/// it — in memory over the whole of it, a hundred and twenty products today (the
/// scale rule's "deferred with its number").
/// </summary>
public sealed class CatalogueExportReader(
    IProductCatalogReader products, IAttributeDefinitionReader definitions, ICategoryReader categories) : ICatalogueExport
{
    public async Task<IReadOnlyList<ExportedProduct>> ProductsAsync(string culture, CancellationToken cancellationToken = default)
    {
        var attributes = await definitions.AllAsync(cancellationToken);
        var tree = await categories.AllAsync(cancellationToken);

        return
        [
            .. (await products.LiveAsync(cancellationToken))
                .Where(product => product.Status != ProductStatus.Archived)
                .OrderBy(product => product.Code, StringComparer.Ordinal)
                .Select(product => Export(product, attributes, tree, culture)),
        ];
    }

    public async Task<ExportedProduct?> ProductAsync(string code, string culture, CancellationToken cancellationToken = default)
    {
        if (await products.FindByCodeAsync(code, cancellationToken) is not { Status: not ProductStatus.Archived } product)
            return null;

        return Export(product, await definitions.AllAsync(cancellationToken), await categories.AllAsync(cancellationToken), culture);
    }

    private static ExportedProduct Export(Product product, AttributeDefinitions attributes, CategoryTree tree, string culture) => new(
        product.Code,
        product.Status == ProductStatus.Active,
        product.Name.In(culture),
        product.Slug.In(culture),
        product.Description?.In(culture),
        product.Brand,
        tree.ByCode(product.Category) is { } category
            ? [.. category.Ancestry.Select(tree.ByCode).OfType<Category>().Select(step => new ExportedCategoryStep(step.Code, step.Name.In(culture)))]
            : [],
        [.. product.Images.OrderBy(image => image.SortOrder).Select(image => new ExportedPicture(image.Id.Value, image.Alt?.In(culture)))],
        [
            .. product.Attributes
                .Where(value => attributes.ByCode(value.Code) is not null)
                .Select(value => Attribute(value, attributes.ByCode(value.Code)!, culture)),
        ],
        [
            .. product.Variants
                .Where(variant => variant.Status == VariantStatus.Available)
                .Select(variant => new ExportedVariant(
                    variant.Sku,
                    variant.Barcode,
                    variant.Price,
                    variant.TaxClass,
                    [
                        .. product.VariantAxes
                            .Where(variant.AxisValues.ContainsKey)
                            .Select(axis => new ExportedOption(
                                axis,
                                attributes.ByCode(axis)?.Label.In(culture) ?? axis,
                                variant.AxisValues[axis],
                                attributes.ByCode(axis)?.LabelForOption(variant.AxisValues[axis], culture) ?? variant.AxisValues[axis])),
                    ],
                    [.. product.PicturesOf(variant).Select(image => image.Id.Value)])),
        ])
    {
        References = [.. product.ExternalReferences.Select(reference => new ExportedReference(reference.Source, reference.ExternalId))],
    };

    /// <summary>The value as the page reads it; a number with its unit, since another vocabulary has no definition to render it by.</summary>
    private static ExportedAttribute Attribute(AttributeValue value, AttributeDefinition definition, string culture)
    {
        var view = ProductAttributes.Render(value, definition, culture);
        var text = value.Kind == AttributeKind.Number && value.Number is { } number
            ? $"{number.ToString(CultureInfo.InvariantCulture)} {definition.Unit}".TrimEnd()
            : view.Value;
        return new ExportedAttribute(value.Code, view.Label, value.Key, text);
    }
}
