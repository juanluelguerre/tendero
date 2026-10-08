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
        IReadOnlyList<SeedImageRow>? Images,
        IReadOnlyDictionary<string, string>? Attributes,
        [property: JsonPropertyName("available_from")] DateOnly? AvailableFrom,
        IReadOnlyList<SeedVariantRow>? Variants = null)
    {
        public ExternalProduct ToExternalProduct(Func<string, Uri> resolveImage) => new(
            ExternalId: ItemId,
            Names: Name,
            Descriptions: Description,
            Brand: Brand,
            Category: ProductType,
            PriceAmount: Price.Amount,
            PriceCurrency: Price.Currency,
            Images: (Images ?? []).Select(image => new ExternalImage(resolveImage(image.File), Shows: image.Shows)).ToList(),
            Attributes: Attributes ?? new Dictionary<string, string>(),
            AvailableFrom: AvailableFrom,
            Variants: Variants is { Count: > 0 }
                ? [.. Variants.Select(variant => new ExternalVariant(variant.Sku, variant.Attributes ?? new Dictionary<string, string>()))]
                : null);
    }

    /// <summary>
    /// A way of buying the product: <c>sku</c> is what tells it apart, and its
    /// <c>attributes</c> are what it varies by in the file's own words.
    /// </summary>
    private sealed record SeedVariantRow(string Sku, IReadOnlyDictionary<string, string>? Attributes);

    /// <summary>
    /// One of the product's <c>images</c>: its file, and — when it is a picture
    /// of some of its variants — what it <c>shows</c>, in the file's own words
    /// (<c>{ "color": "negro" }</c>). Most are just a file, and the file may
    /// still say so with a plain string.
    /// </summary>
    [JsonConverter(typeof(SeedImageRowConverter))]
    private sealed record SeedImageRow(string File, IReadOnlyDictionary<string, string>? Shows = null);

    /// <summary>Reads an image written either way: <c>"images/x.webp"</c> or <c>{ "file": "images/x.webp", "shows": { … } }</c>.</summary>
    private sealed class SeedImageRowConverter : JsonConverter<SeedImageRow>
    {
        public override SeedImageRow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new SeedImageRow(reader.GetString()!);

            using var document = JsonDocument.ParseValue(ref reader);
            var image = document.RootElement;
            return new SeedImageRow(
                image.GetProperty("file").GetString()!,
                image.TryGetProperty("shows", out var shows) ? shows.Deserialize<Dictionary<string, string>>(options) : null);
        }

        public override void Write(Utf8JsonWriter writer, SeedImageRow value, JsonSerializerOptions options) =>
            throw new NotSupportedException("The seed is read, never written.");
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
