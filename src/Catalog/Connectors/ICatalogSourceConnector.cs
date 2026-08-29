using Tendero.SharedKernel;

namespace Tendero.Catalog.Connectors;

/// <summary>
/// Puerto de entrada de catálogo. Cada origen (seed, shopify, medusa, prestashop...)
/// implementa este contrato y se registra como keyed service con su nombre de Source.
/// El slice ImportProducts solo conoce esta interfaz, nunca un origen concreto.
/// </summary>
public interface ICatalogSourceConnector
{
    /// <summary>Identificador estable en minúsculas: "seed", "shopify", "medusa"...</summary>
    string Source { get; }

    /// <summary>
    /// Stream de productos del origen. IAsyncEnumerable a propósito:
    /// un catálogo de 150k productos no debe cargarse entero en memoria.
    /// </summary>
    IAsyncEnumerable<ExternalProduct> StreamProductsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// DTO del contrato, multilenguaje desde el origen: los textos llegan como
/// diccionarios cultura -> valor ("es", "en"). Si un origen solo tiene un idioma
/// (p. ej. una tienda Shopify solo en inglés), entrega esa única clave y el
/// slice de enriquecimiento con IA completará las traducciones que falten.
/// </summary>
public sealed record ExternalProduct(
    string ExternalId,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string>? Descriptions,
    string? Brand,
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    IReadOnlyList<Uri> ImageUrls,
    IReadOnlyDictionary<string, string> Attributes)
{
    public Money Price => new(PriceAmount, PriceCurrency);

    public LocalizedText LocalizedName => new(Names);

    public LocalizedText? LocalizedDescription =>
        Descriptions is { Count: > 0 } ? new LocalizedText(Descriptions) : null;
}
