using Tendero.Catalog.Domain;
using Tendero.SharedKernel;

namespace Tendero.Search.Contracts;

/// <summary>
/// Puerto de escritura: proyectar productos al motor de búsqueda.
/// Elasticsearch es un detalle de infraestructura detrás de esta interfaz.
/// </summary>
public interface IProductIndexer
{
    Task IndexAsync(Product product, CancellationToken ct = default);
    Task RemoveAsync(ProductId productId, CancellationToken ct = default);
}

/// <summary>
/// Puerto de lectura léxica (BM25). Cuando llegue la búsqueda híbrida
/// habrá otro puerto que componga este con el vectorial; este NO cambia:
/// es el modo degradado si la capa de IA se cae.
/// </summary>
public interface ILexicalProductSearch
{
    Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default);
}

public sealed record ProductSearchQuery(string Text, string Culture, int Page = 1, int PageSize = 20);

public sealed record SearchHit(
    string ProductId,
    string Name,
    string Slug,
    string? Brand,
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    string? ImageUrl,
    double Score);

public sealed record SearchResultPage(
    IReadOnlyList<SearchHit> Hits,
    long Total,
    int Page,
    int PageSize,
    double TookMs);

/// <summary>
/// Documento plano por (producto, cultura). Un producto vive en products_es
/// y en products_en con su texto ya resuelto: el análisis lingüístico
/// (stemming, stopwords) es por índice, no por campo condicional.
/// </summary>
public sealed record ProductSearchDocument
{
    public required string Id { get; init; }              // ProductId
    public required string Culture { get; init; }         // "es" | "en"
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }
    public string? Category { get; init; }

    /// <summary>Atributos aplanados a texto buscable: "color azul marino talla 36-42 drop 8".</summary>
    public string? AttributesText { get; init; }

    public required string Slug { get; init; }
    public decimal PriceAmount { get; init; }
    public required string PriceCurrency { get; init; }
    public string? ImageUrl { get; init; }
    public required string Status { get; init; }          // solo "active" es buscable

    public static string IndexNameFor(string culture) => $"products_{culture}";

    public static ProductSearchDocument FromProduct(Product product, string culture) => new()
    {
        Id = product.Id.ToString(),
        Culture = culture,
        Name = product.Name.In(culture),
        Description = product.Description?.In(culture),
        Brand = product.Brand,
        Category = product.Category,
        AttributesText = product.Attributes.Count == 0
            ? null
            : string.Join(' ', product.Attributes.Select(kv => $"{kv.Key} {kv.Value}")),
        Slug = product.Slug.In(culture),
        PriceAmount = product.Price.Amount,
        PriceCurrency = product.Price.Currency,
        ImageUrl = product.Images.FirstOrDefault()?.Url.ToString(),
        Status = product.Status.ToString().ToLowerInvariant()
    };
}
