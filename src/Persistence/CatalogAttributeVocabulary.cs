using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence;

// Where Catalog's attribute definitions become the values Knowledge takes (ADR
// 0014): here rather than in one host, because the worker proposes claims and
// the API may ask for a proposal again, and both need the same vocabulary.

/// <summary>
/// Catalog's attribute definitions as the specs a claim about one product may be
/// typed with. Three things narrow them, each said by the catalogue and none
/// listed here:
///
/// - **the product's category** (ADR 0056): what its branch declares. A product
///   with no category, or one whose branch declares nothing, is offered every
///   attribute — nobody has said otherwise, and that is how it was before;
/// - **what the source can say**: a picture's reading is offered only the
///   attributes that say of themselves that a picture shows them;
/// - **what no source is asked for**: the variant axes and the size range come
///   from the import and the variants — as they did in P10-0's golden set.
/// </summary>
internal sealed class CatalogAttributeVocabulary(
    IAttributeDefinitionReader definitions, ICategoryReader categories, IProductRepository products) : IAttributeVocabulary
{
    private static readonly HashSet<string> NotFromText = new(StringComparer.OrdinalIgnoreCase) { "SIZE_RANGE" };

    public async Task<IReadOnlyList<AttributeSpec>> CurrentAsync(
        ProductId product, SourceKind readFrom, CancellationToken cancellationToken = default)
    {
        var all = await definitions.AllAsync(cancellationToken);
        var category = (await products.FindByIdAsync(product, cancellationToken))?.Category;
        var declared = (await categories.AllAsync(cancellationToken)).AttributesOf(category);

        return
        [
            .. all.All
                .Where(definition => !definition.IsVariantAxis && !NotFromText.Contains(definition.Code))
                .Where(definition => declared is null || declared.Contains(definition.Code, StringComparer.OrdinalIgnoreCase))
                .Where(definition => readFrom != SourceKind.ProductImage || definition.IsShownInPictures)
                .Select(definition => new AttributeSpec(
                    definition.Code,
                    definition.Kind.ToString(),
                    definition.Unit,
                    definition.Label.In("en"),
                    [.. definition.Options.Select(option => option.Code)],
                    definition.Label.In("es"))),
        ];
    }
}
