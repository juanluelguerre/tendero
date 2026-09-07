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
public sealed record SeedProduct(
    string ItemId,
    string EnglishName,
    string EnglishDescription,
    string? SpanishColour);

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

    public static IReadOnlyList<SeedProduct> Read(string path)
    {
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"'{path}' is empty or not a product array.");

        return
        [
            .. rows
                .Where(row => !string.IsNullOrWhiteSpace(row.ItemId))
                .Select(row => new SeedProduct(
                    row.ItemId,
                    Text(row.Name),
                    Text(row.Description),
                    row.Attributes?.GetValueOrDefault(ColourKey)))
        ];
    }

    private static string Text(Dictionary<string, string>? localized) =>
        localized?.GetValueOrDefault("en") ?? string.Empty;

    private sealed record Row(
        string ItemId,
        Dictionary<string, string>? Name,
        Dictionary<string, string>? Description,
        Dictionary<string, string>? Attributes);
}
