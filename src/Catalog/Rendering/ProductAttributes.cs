using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Catalog.Rendering;

/// <summary>
/// A rendered attribute. The VALUE is resolved here because it needs the
/// definition; the WORDING is not, because "Sí"/"Yes" is interface copy and
/// belongs in the frontend's translation files, never in an API response.
/// </summary>
public sealed record ProductAttributeView(
    string Code,
    string Label,
    AttributeKind Kind,
    string? Value,
    decimal? Number,
    string? Unit,
    bool? Flag);

/// <summary>An attribute a product's category requires and the product has no value for, named in a culture.</summary>
public sealed record MissingAttributeView(string Code, string Label);

/// <summary>
/// How a product's attribute reads in a culture. Moved down from GetProduct when
/// the backoffice's review (the redesign's R-4.3) needed the same reading: a
/// slice never references another, so what two slices share lives below both.
/// </summary>
public static class ProductAttributes
{
    public static ProductAttributeView Render(
        AttributeValue value, AttributeDefinition? definition, string culture) =>
        new(value.Code,
            // The product page drops values with no definition before it gets
            // here, and the review shows them by their code: the rendering rule
            // is not the filtering one, and a renderer that threw on a null
            // definition would be a worse shape.
            definition?.Label.In(culture) ?? value.Code,
            value.Kind,
            value.Kind is AttributeKind.Number or AttributeKind.Boolean
                ? null
                : value.RenderIn(culture, definition),
            value.Number,
            definition?.Unit,
            value.Flag);

    /// <summary>
    /// What the product lacks of what its category requires (ADR 0056, amended),
    /// each by its label — or by its code, when the catalogue never defined it:
    /// a gap is shown, not hidden behind a second gap.
    /// </summary>
    public static IReadOnlyList<MissingAttributeView> Missing(
        Product product, CategoryTree tree, AttributeDefinitions definitions, string culture) =>
        [.. product.MissingRequired(tree).Select(code => new MissingAttributeView(code, definitions.ByCode(code)?.Label.In(culture) ?? code))];
}
