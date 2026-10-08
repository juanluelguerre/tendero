using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// One product, reduced to what a picture of it needs.
/// </summary>
/// <param name="ItemId">The source id, which is also the file name: the seed
/// already points every product at <c>images/{item_id}.webp</c>, so a picture is
/// a file with the right name and nothing else.</param>
/// <param name="EnglishDescription">What the object is. The English one on
/// purpose: it describes the thing without the invented brand name, which is
/// what a generator reads best.</param>
/// <param name="SpanishColour">The label the catalogue declares, or null.
/// **Thirty-five products declare none**, and that is not missing data — the
/// packing cubes are three cubes of three colours, and the attribute was removed
/// rather than a lie chosen.</param>
/// <param name="Pieces">How many items the product is, where the catalogue says
/// so (`piezas`: a set of three pans, one cast iron pan) — what a picture's count
/// is held to. Null for most: a pair of shoes declares none.</param>
/// <param name="VariantOf">For a picture of one of a product's variants that is
/// not its cover — the same product in another colour: the product's id. The
/// entry is then that picture, named for its file, with the variant's colour.</param>
public sealed record SeedProduct(
    string ItemId,
    string EnglishName,
    string EnglishDescription,
    string? SpanishColour,
    int? Pieces = null,
    string? VariantOf = null);

/// <summary>
/// Reads <c>seed/products.sample.json</c>.
///
/// The file is snake_case, so this does NOT use the repository's usual
/// <c>JsonSerializerDefaults.Web</c> options — the same exception, for the same
/// reason, that <c>SeedCatalogConnector</c> already makes.
/// </summary>
public static class ProductCatalogue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>The attribute key. Lowercase Spanish, because the seed speaks in
    /// the connector's aliases and not in the definitions' codes.</summary>
    private const string ColourKey = "color";

    private const string PiecesKey = "piezas";

    public static IReadOnlyList<SeedProduct> Read(string path)
    {
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"'{path}' is empty or not a product array.");

        return [.. rows.Where(row => !string.IsNullOrWhiteSpace(row.ItemId)).SelectMany(Pictures)];
    }

    /// <summary>
    /// What a row has to be drawn: the product, and one more picture for each
    /// variant that names a picture of its own other than the cover. A product
    /// sold in two colours says its colours on its variants and not on itself,
    /// so the cover's colour is the colour of the variant that shows it.
    /// </summary>
    private static IEnumerable<SeedProduct> Pictures(Row row)
    {
        var pieces = int.TryParse(row.Attributes?.GetValueOrDefault(PiecesKey), out var count) ? count : (int?)null;
        string? ColourOf(int place) => row.Variants?.FirstOrDefault(variant => (variant.Image ?? 0) == place)?.Attributes?.GetValueOrDefault(ColourKey);

        yield return new SeedProduct(
            row.ItemId, Text(row.Name), Text(row.Description), row.Attributes?.GetValueOrDefault(ColourKey) ?? ColourOf(0), pieces);

        foreach (var place in (row.Variants ?? []).Select(variant => variant.Image ?? 0).Where(place => place > 0).Distinct())
        {
            if (row.Images is { } images && place < images.Count)
            {
                yield return new SeedProduct(
                    Path.GetFileNameWithoutExtension(images[place]), Text(row.Name), Text(row.Description), ColourOf(place), pieces, VariantOf: row.ItemId);
            }
        }
    }

    private static string Text(Dictionary<string, string>? localized) =>
        localized?.GetValueOrDefault("en") ?? string.Empty;

    private sealed record Row(
        string ItemId,
        Dictionary<string, string>? Name,
        Dictionary<string, string>? Description,
        Dictionary<string, string>? Attributes,
        List<string>? Images = null,
        List<VariantRow>? Variants = null);

    private sealed record VariantRow(string Sku, Dictionary<string, string>? Attributes, int? Image = null);
}
