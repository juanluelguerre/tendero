using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Connectors.Seed;

public sealed class SeedConnectorOptions
{
    public const string SectionName = "Catalog:Connectors:Seed";

    /// <summary>Path to the dataset's JSON. By default, the sample committed to the repo.</summary>
    public string FilePath { get; set; } = "seed/products.sample.json";
}

/// <summary>
/// The repository's default connector: anybody who clones can run the import
/// without signing up for anything. It reads the JSON as a stream with
/// DeserializeAsyncEnumerable, so the full ABO file (~147k products) never goes
/// through memory at once.
/// </summary>
internal sealed class SeedCatalogConnector(IOptions<SeedConnectorOptions> options) : ICatalogSourceConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>The DI registration key and the source name. It used to be
    /// written twice — the literal in <c>AddKeyedScoped</c> and this
    /// <c>Source</c> — and if they diverge the import fails at runtime, not at
    /// compile time.</summary>
    public const string Key = "seed";

    public string Source => Key;

    // Resolved once and not per image: it is the same path for the whole file.
    private string SeedDirectory =>
        this.seedDirectory ??= Path.GetDirectoryName(Path.GetFullPath(options.Value.FilePath)) ?? ".";

    private string? seedDirectory;

    public async IAsyncEnumerable<ExternalProduct> StreamProductsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(options.Value.FilePath);

        await foreach (var row in JsonSerializer
            .DeserializeAsyncEnumerable<SeedProductRow>(stream, JsonOptions, cancellationToken))
        {
            if (row is null || String.IsNullOrWhiteSpace(row.ItemId) || row.Name.Count == 0)
                continue;

            yield return row.ToExternalProduct(ResolveImage);
        }
    }

    /// <summary>
    /// The shape of the seed file. Texts are culture -> value dictionaries, like
    /// the contract; the Amazon Berkeley Objects schema (arrays with
    /// language_tag) is converted into this shape by the download script.
    /// </summary>
    private sealed record SeedProductRow(
        [property: JsonPropertyName("item_id")] string ItemId,
        IReadOnlyDictionary<string, string> Name,
        IReadOnlyDictionary<string, string>? Description,
        string? Brand,
        [property: JsonPropertyName("product_type")] string? ProductType,
        SeedPrice Price,
        IReadOnlyList<string>? Images,
        IReadOnlyDictionary<string, string>? Attributes,
        [property: JsonPropertyName("available_from")] DateOnly? AvailableFrom)
    {
        public ExternalProduct ToExternalProduct(Func<string, Uri> resolveImage) => new(
            ExternalId: ItemId,
            Names: Name,
            Descriptions: Description,
            Brand: Brand,
            Category: ProductType,
            PriceAmount: Price.Amount,
            PriceCurrency: Price.Currency,
            Images: (Images ?? []).Select(resolveImage).Select(location => new ExternalImage(location)).ToList(),
            Attributes: Attributes ?? new Dictionary<string, string>(),
            AvailableFrom: AvailableFrom);
    }

    private sealed record SeedPrice(decimal Amount, string Currency);

    /// <summary>
    /// The seed file references its images relatively ("images/X.png"), and they
    /// resolve against its own directory: it is a catalogue source that lives on
    /// disk, just as Shopify serves its own from a CDN. An absolute URL is
    /// respected as-is, so a remote source can be pointed at.
    /// </summary>
    private Uri ResolveImage(string reference)
    {
        if (Uri.TryCreate(reference, UriKind.Absolute, out var absolute))
            return absolute;

        return new Uri(Path.GetFullPath(Path.Combine(SeedDirectory, reference)));
    }
}
