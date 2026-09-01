using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Connectors.Seed;

public sealed class SeedConnectorOptions
{
    public const string SectionName = "Catalog:Connectors:Seed";

    /// <summary>Ruta al JSON del dataset. Por defecto, la muestra versionada en el repo.</summary>
    public string FilePath { get; set; } = "seed/products.sample.json";
}

/// <summary>
/// Conector por defecto del repo: cualquiera que clone puede ejecutar
/// la importación sin darse de alta en nada. Lee el JSON en streaming
/// con DeserializeAsyncEnumerable, así el fichero completo de ABO
/// (~147k productos) no pasa por memoria de golpe.
/// </summary>
internal sealed class SeedCatalogConnector(IOptions<SeedConnectorOptions> options) : ICatalogSourceConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Clave de registro en DI y nombre del origen. Estaba escrita dos
    /// veces —el literal del <c>AddKeyedScoped</c> y este <c>Source</c>— y si
    /// divergen la importación falla en runtime, no al compilar.</summary>
    public const string Key = "seed";

    public string Source => Key;

    // Se resuelve una vez y no por imagen: es la misma ruta para todo el fichero.
    private string SeedDirectory =>
        _seedDirectory ??= Path.GetDirectoryName(Path.GetFullPath(options.Value.FilePath)) ?? ".";

    private string? _seedDirectory;

    public async IAsyncEnumerable<ExternalProduct> StreamProductsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(options.Value.FilePath);

        await foreach (var row in JsonSerializer
            .DeserializeAsyncEnumerable<SeedProductRow>(stream, JsonOptions, cancellationToken))
        {
            if (row is null || string.IsNullOrWhiteSpace(row.ItemId) || row.Name.Count == 0)
                continue;

            yield return row.ToExternalProduct(ResolveImage);
        }
    }

    /// <summary>
    /// Forma del fichero seed. Los textos son diccionarios cultura -> valor,
    /// igual que el contrato; el esquema de Amazon Berkeley Objects (arrays con
    /// language_tag) se convierte a esta forma en el script de descarga.
    /// </summary>
    private sealed record SeedProductRow(
        [property: JsonPropertyName("item_id")] string ItemId,
        IReadOnlyDictionary<string, string> Name,
        IReadOnlyDictionary<string, string>? Description,
        string? Brand,
        [property: JsonPropertyName("product_type")] string? ProductType,
        SeedPrice Price,
        IReadOnlyList<string>? Images,
        IReadOnlyDictionary<string, string>? Attributes)
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
            Attributes: Attributes ?? new Dictionary<string, string>());
    }

    private sealed record SeedPrice(decimal Amount, string Currency);

    /// <summary>
    /// El fichero seed referencia sus imágenes en relativo ("images/X.png"), y
    /// se resuelven contra su propio directorio: es un origen de catálogo que
    /// vive en disco, igual que Shopify sirve las suyas desde su CDN. Una URL
    /// absoluta se respeta tal cual, para poder apuntar a un origen remoto.
    /// </summary>
    private Uri ResolveImage(string reference)
    {
        if (Uri.TryCreate(reference, UriKind.Absolute, out var absolute))
            return absolute;

        return new Uri(Path.GetFullPath(Path.Combine(SeedDirectory, reference)));
    }
}
