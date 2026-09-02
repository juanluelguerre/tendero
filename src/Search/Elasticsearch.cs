using System.Diagnostics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Elasticsearch;

/// <summary>
/// Qué analizador nativo de Elasticsearch usa cada cultura. Detalle del motor y
/// por eso internal: fuera de aquí lo que se conoce es
/// <see cref="SearchCultures.Supported"/>, no que exista un stemmer llamado
/// "spanish".
/// </summary>
internal static class CultureAnalyzers
{
    public static readonly IReadOnlyDictionary<string, string> ByCulture = new Dictionary<string, string>
    {
        ["es"] = "spanish",   // analizadores nativos de ES: stemming + stopwords
        ["en"] = "english"
    };
}

/// <summary>Crea products_es y products_en al arrancar si no existen (idempotente).</summary>
internal sealed class SearchIndexInitializer(
    ElasticsearchClient client,
    ILogger<SearchIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (culture, analyzer) in CultureAnalyzers.ByCulture)
        {
            var index = ProductSearchDocument.IndexNameFor(culture);
            var exists = await client.Indices.ExistsAsync(index, cancellationToken);
            if (exists.Exists) continue;

            var response = await client.Indices.CreateAsync(index, c => c
                .Mappings(m => m.Properties<ProductSearchDocument>(p => p
                    .Keyword(d => d.Id)
                    // El campo del collapse: tiene que ser keyword, no text.
                    .Keyword(d => d.ProductId)
                    .Keyword(d => d.Sku)
                    .Keyword(d => d.AxisValues)
                    .Keyword(d => d.Culture)
                    .Text(d => d.Name, t => t.Analyzer(analyzer))
                    .Text(d => d.Description!, t => t.Analyzer(analyzer))
                    .Text(d => d.Brand!, t => t.Fields(f => f.Keyword("raw")))
                    .Keyword(d => d.Category!)
                    .Text(d => d.AttributesText!, t => t.Analyzer(analyzer))
                    .Keyword(d => d.Slug)
                    .Keyword(d => d.ImageId!)
                    .DoubleNumber(d => d.PriceAmount)
                    .DoubleNumber(d => d.PriceFrom)
                    .DoubleNumber(d => d.PriceTo)
                    .Keyword(d => d.PriceCurrency)
                    .Keyword(d => d.Status))),
                cancellationToken);

            if (!response.IsValidResponse)
                throw new InvalidOperationException($"Could not create index '{index}': {response.DebugInformation}");

            logger.LogInformation("Search index {Index} created with analyzer {Analyzer}", index, analyzer);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Construye el documento y lo escribe. Necesita las definiciones de atributo
/// porque el texto buscable se renderiza EN LA CULTURA DEL ÍNDICE: sin ellas,
/// products_en contendría "color azul marino" y ninguna consulta inglesa podría
/// casarlo — que es exactamente por qué "navy blue shoes" puntúa 0.000 en la
/// línea base commiteada.
/// </summary>
internal sealed class ElasticsearchProductIndexer(
    ElasticsearchClient client, IAttributeDefinitionReader attributeDefinitions) : IProductIndexer
{
    public async Task IndexAsync(Product product, CancellationToken ct = default)
    {
        var definitions = await attributeDefinitions.AllAsync(ct);

        foreach (var culture in SearchCultures.Supported)
        {
            // Reindexar un producto es reemplazar TODAS sus variantes, no añadir:
            // si una se retira, su documento tiene que desaparecer, y escribir
            // sólo las vivas dejaría la retirada ahí para siempre.
            await RemoveAsync(product.Id, culture, ct);

            foreach (var document in ProductSearchDocument.ForVariants(product, culture, definitions))
            {
                var response = await client.IndexAsync(
                    document,
                    i => i.Index(ProductSearchDocument.IndexNameFor(culture)).Id(document.Id),
                    ct);

                // SearchUnavailableException y no InvalidOperationException: un
                // fallo del motor es 503 y reintentable. Salía como 500 por este
                // camino y como 503 por el de consulta, para la misma avería.
                if (!response.IsValidResponse)
                    throw new SearchUnavailableException(
                        $"indexing {document.Id} into {culture}", response.DebugInformation);
            }
        }
    }

    public async Task RemoveAsync(ProductId productId, CancellationToken ct = default)
    {
        foreach (var culture in SearchCultures.Supported)
            await RemoveAsync(productId, culture, ct);
    }

    /// <summary>
    /// Borra por CONSULTA y no por id: el documento ya no es el producto sino
    /// cada una de sus variantes, y cuántas hay no se sabe desde aquí.
    /// </summary>
    private async Task RemoveAsync(ProductId productId, string culture, CancellationToken ct)
    {
        {
            var response = await client.DeleteByQueryAsync<ProductSearchDocument>(
                ProductSearchDocument.IndexNameFor(culture),
                d => d.Query(q => q.Term(t => t.Field(f => f.ProductId).Value(productId.ToString()))),
                ct);

            // Borrar lo que no está indexado es idempotente y es el caso normal
            // de un producto que nunca se publicó: delete_by_query devuelve cero
            // borrados, no un error. Cualquier fallo real no puede ignorarse —
            // éste es el camino por el que Archive() saca un producto del
            // catálogo, y tragarse un 503 dejaría indexado lo que se retiró.
            if (response.IsValidResponse)
                return;

            throw new SearchUnavailableException(
                $"removing {productId} from {culture}", response.DebugInformation);
        }
    }
}

internal sealed class ElasticsearchLexicalSearch(ElasticsearchClient client) : ILexicalProductSearch
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    /// <summary>
    /// Campos de texto con sus boosts. `category` NO esta: se mapea como keyword
    /// y solo casaria con el termino exacto ("COFFEE_MAKER"), asi que listarlo
    /// entre campos de texto prometia una busqueda que nunca ocurria. Volvera
    /// cuando la taxonomia sea texto de cara al usuario y no un codigo.
    /// </summary>
    private static readonly string[] SearchableFields =
        ["name^3", "brand^2", "attributesText^2", "description"];

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        using var activity = Telemetry.StartActivity("search.lexical");
        activity?.SetTag("search.culture", query.Culture);
        activity?.SetTag("search.text", query.Text);

        var response = await client.SearchAsync<ProductSearchDocument>(s => s
            .Indices(ProductSearchDocument.IndexNameFor(query.Culture))
            .From((query.Page - 1) * query.PageSize)
            .Size(query.PageSize)
            // Colapsar por producto: el matching y los filtros ocurren por
            // variante —que es lo que los hace exactos— y el resultado vuelve
            // como producto, con la variante que ganó dentro.
            .Collapse(c => c.Field(d => d.ProductId))
            // El total de `hits` cuenta DOCUMENTOS, y aquí un documento es una
            // variante. El número que la interfaz enseña es de productos, así
            // que sale de una cardinality sobre el campo colapsado. Es
            // aproximada por encima de 40.000 grupos, exacta muy por debajo.
            .Aggregations(a => a.Add("products", agg => agg.Cardinality(c => c.Field(d => d.ProductId))))
            .Query(q => q.Bool(b => b
                // Dos formas de casar la misma consulta, unidas por should. Cada
                // una cubre lo que la otra no puede, y eso NO es adorno: el
                // golden set mide las dos (ver docs/search-evaluation.md).
                .Must(m => m.Bool(alternatives => alternatives
                    .Should(
                        // cross_fields: los terminos pueden repartirse entre campos.
                        // "zapatillas running mujer" tiene las dos primeras en name
                        // y la tercera en attributesText; con best_fields no casaba
                        // ninguna, porque exigia todas en UN campo.
                        should => should.MultiMatch(mm => mm
                            .Query(query.Text)
                            .Fields(SearchableFields)
                            .Type(TextQueryType.CrossFields)
                            .Operator(Operator.And)),
                        // best_fields con fuzziness: tolera erratas ("zapatilas").
                        // Va aparte porque cross_fields NO admite fuzziness.
                        should => should.MultiMatch(mm => mm
                            .Query(query.Text)
                            .Fields(SearchableFields)
                            .Fuzziness(new Fuzziness("AUTO"))
                            .Operator(Operator.And)))
                    .MinimumShouldMatch(1)))
                .Filter(f => f.Term(t => t.Field(d => d.Status).Value("active"))))),
            ct);

        if (!response.IsValidResponse)
            throw new SearchUnavailableException("query", response.DebugInformation);

        var hits = response.Hits.Select(h => new SearchHit(
            h.Source!.ProductId,
            h.Source.Name,
            h.Source.Slug,
            h.Source.Brand,
            h.Source.Category,
            h.Source.Id,
            h.Source.Sku,
            h.Source.PriceAmount,
            h.Source.PriceFrom,
            h.Source.PriceTo,
            h.Source.PriceCurrency,
            h.Source.ImageId,
            h.Score ?? 0d)).ToList();

        var products = response.Aggregations?.GetCardinality("products")?.Value is { } value
            ? (long)value
            : hits.Count;

        activity?.SetTag("search.total", products);
        activity?.SetTag("search.variants", response.Total);
        activity?.SetTag("search.took_ms", response.Took);

        return new SearchResultPage(hits, products, query.Page, query.PageSize, response.Took);
    }
}
