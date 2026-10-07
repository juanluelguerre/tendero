using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;

namespace ElGuerre.Tendero.Persistence;

// Where Catalog's attribute definitions become the values Knowledge takes (ADR
// 0014): here rather than in one host, because the worker proposes claims and
// the API may ask for a proposal again, and both need the same vocabulary.

/// <summary>
/// Catalog's attribute definitions as the specs a claim may be typed with.
/// The variant axes and the size range come from the import and the variants,
/// not from a description, and are left out — as they were in P10-0's golden set.
/// </summary>
internal sealed class CatalogAttributeVocabulary(IAttributeDefinitionReader definitions) : IAttributeVocabulary
{
    private static readonly HashSet<string> NotFromText = new(StringComparer.OrdinalIgnoreCase) { "SIZE_RANGE" };

    /// <summary>
    /// What a picture can show of what the catalogue has a name for: how many
    /// there are, and nothing else today (the author's ruling, 2026-10-07). Not a
    /// power, a capacity or a colour temperature; not a material, which a flat
    /// illustration does not tell. Its colour is a variant axis, offered to no
    /// source. Listed here beside the other exclusion until an attribute's
    /// definition can say it of itself — the catalogue's new families will bring
    /// attributes a picture does show (a closure, a pattern), and that is when.
    /// </summary>
    private static readonly HashSet<string> ShownInPictures = new(StringComparer.OrdinalIgnoreCase) { "PIECES" };

    public async Task<IReadOnlyList<AttributeSpec>> CurrentAsync(SourceKind readFrom, CancellationToken cancellationToken = default)
    {
        var all = await definitions.AllAsync(cancellationToken);
        return
        [
            .. all.All
                .Where(definition => !definition.IsVariantAxis && !NotFromText.Contains(definition.Code))
                .Where(definition => readFrom != SourceKind.ProductImage || ShownInPictures.Contains(definition.Code))
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
