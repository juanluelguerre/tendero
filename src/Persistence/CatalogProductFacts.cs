using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Knowledge.Ports;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Catalog's products as the facts Knowledge's specs table and comparison take
/// (ADR 0014), beside <see cref="CatalogAttributeVocabulary"/> for the same
/// reason: Persistence already knows both contexts, and both hosts need it.
///
/// The filter is the product page's, on purpose: an active product only, and an
/// attribute only when the catalogue defines it — a table that showed what the
/// page hides would be a second page contradicting the first.
/// </summary>
internal sealed class CatalogProductFacts(
    IProductCatalogReader products,
    IAttributeDefinitionReader definitions) : IProductFacts
{
    public async Task<IReadOnlyList<ProductFacts>> OfAsync(
        IReadOnlyList<string> productCodes, string culture, CancellationToken cancellationToken = default)
    {
        var attributes = await definitions.AllAsync(cancellationToken);
        var result = new List<ProductFacts>(productCodes.Count);

        // One lookup per code: a comparison holds four at most (P10-7).
        foreach (var code in productCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var product = await products.FindByCodeAsync(ProductCode.Normalise(code), cancellationToken);
            if (product is not { Status: ProductStatus.Active })
                continue;

            result.Add(new ProductFacts(
                product.Id,
                product.Code,
                product.Name.In(culture),
                product.Slug.In(culture),
                [.. product.Attributes
                    .Select(value => (value, definition: attributes.ByCode(value.Code)))
                    .Where(pair => pair.definition is not null)
                    .Select(pair => Fact(pair.value, pair.definition!, culture))
                    .OrderBy(fact => fact.Label, StringComparer.OrdinalIgnoreCase)]));
        }

        return result;
    }

    private static ProductFact Fact(AttributeValue value, AttributeDefinition definition, string culture) => new(
        definition.Code,
        definition.Label.In(culture),
        KindOf(value.Kind),
        value.Kind is AttributeKind.Number or AttributeKind.Boolean ? null : value.RenderIn(culture, definition),
        value.Number,
        definition.Unit,
        value.Flag,
        value.Key);

    /// <summary>Catalog's kind in Knowledge's words: the same members, two
    /// types, because neither context references the other.</summary>
    private static FactKind KindOf(AttributeKind kind) => kind switch
    {
        AttributeKind.Text => FactKind.Text,
        AttributeKind.LocalizedText => FactKind.LocalizedText,
        AttributeKind.Number => FactKind.Number,
        AttributeKind.Boolean => FactKind.Boolean,
        AttributeKind.Option => FactKind.Option,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An attribute kind Knowledge has no word for."),
    };
}
