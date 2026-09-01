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
/// Lectura del catálogo desde el lado de búsqueda. Vivía dentro del slice
/// ProjectProductToIndex mientras fue el único que leía; con ReindexProducts
/// pasa a ser compartido, y un slice no puede referenciar a otro.
///
/// <c>StreamAllAsync</c> devuelve TODOS los productos, no sólo los Active: el
/// reindexado aplica la misma regla que la proyección (Active se indexa, el
/// resto se retira), y así converge el índice a la verdad en vez de limitarse a
/// añadir. Filtrar por Active aquí dejaría documentos rancios de lo que dejó de
/// estarlo.
/// </summary>
public interface IProductReader
{
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct);
    IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct);
}

/// <summary>
/// Las culturas que el buscador sirve, y el orden en que se recorren. Es
/// CONTRATO: la herramienta de evaluación necesita saber contra qué índices
/// puntuar, y el inicializador necesita saber cuáles crear.
///
/// Vivía junto al mapa de analizadores dentro del adaptador de Elasticsearch, y
/// son dos cosas distintas: que el catálogo hable español e inglés es una
/// decisión de producto; que "es" se analice con el stemmer <c>spanish</c> es un
/// detalle del motor, y ése se queda dentro del adaptador.
/// </summary>
public static class SearchCultures
{
    public static readonly string[] Supported = ["es", "en"];

    public static IEnumerable<string> IndexNames =>
        Supported.Select(ProductSearchDocument.IndexNameFor);
}

/// <summary>
/// La regla del índice, en un solo sitio: sólo lo Active es buscable, y lo que
/// deja de estarlo se retira. Estaba escrita dos veces —en la proyección del
/// outbox y en el reindexado— con un comentario que decía "replica la regla en
/// vez de inventar otra", que es la forma educada de decir que hay dos.
///
/// Que sean dos importa: si divergen, el reindexado deja de converger a lo mismo
/// que produce la proyección, y la diferencia sólo se ve buscando.
/// </summary>
public static class ProductIndexProjection
{
    /// <summary>Devuelve true si el producto quedó indexado, false si se retiró.</summary>
    public static async Task<bool> ApplyAsync(
        IProductIndexer indexer, Product product, CancellationToken ct)
    {
        if (product.Status == ProductStatus.Active)
        {
            await indexer.IndexAsync(product, ct);
            return true;
        }

        await indexer.RemoveAsync(product.Id, ct);
        return false;
    }
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

/// <summary>
/// El motor de búsqueda no ha podido responder. Es un tipo propio y no una
/// InvalidOperationException porque la API tiene que distinguirla: un fallo del
/// motor es 503 y se reintenta, no un 500 que sugiere un error de programación.
///
/// <paramref name="diagnostics"/> es el audit trail del cliente de Elastic, que
/// es largo y contiene rutas y puertos internos: se conserva porque es lo que
/// hace rápido el diagnóstico en el log, y por eso mismo no puede acabar en el
/// cuerpo de una respuesta HTTP.
/// </summary>
public sealed class SearchUnavailableException(string operation, string diagnostics)
    : Exception($"Search backend failed during {operation}. {diagnostics}")
{
    public string Operation { get; } = operation;
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
    string? ImageId,
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
    /// <summary>Clave de la imagen en el almacén, no una URL. La URL la compone
    /// el borde HTTP, así que meter un CDN delante no toca ni el índice ni el
    /// dominio (docs/adr/0011-product-images.md).</summary>
    public string? ImageId { get; init; }
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
        // Portada = la de menor SortOrder, y esa regla vive en el agregado. Aquí
        // se cogía la primera de la lista mientras el listado del backoffice
        // ordenaba: el mismo producto podía enseñar dos fotos distintas.
        ImageId = product.PrimaryImage?.Id.Value,
        Status = product.Status.ToString().ToLowerInvariant()
    };
}
