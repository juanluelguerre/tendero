// Paquete: Elastic.Clients.Elasticsearch (9.x). Si la API fluida difiere en tu
// versión, la forma de las peticiones es lo estable: mapping por idioma,
// multi_match con boosts y filtro por status.
using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tendero.Catalog.Domain;
using Tendero.Search.Contracts;
using Tendero.SharedKernel;

namespace Tendero.Search.Elasticsearch;

public static class SearchCultures
{
    public static readonly IReadOnlyDictionary<string, string> Analyzers = new Dictionary<string, string>
    {
        ["es"] = "spanish",   // analizadores nativos de ES: stemming + stopwords
        ["en"] = "english"
    };
}

/// <summary>Crea products_es y products_en al arrancar si no existen (idempotente).</summary>
public sealed class SearchIndexInitializer(
    ElasticsearchClient client,
    ILogger<SearchIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (culture, analyzer) in SearchCultures.Analyzers)
        {
            var index = ProductSearchDocument.IndexNameFor(culture);
            var exists = await client.Indices.ExistsAsync(index, cancellationToken);
            if (exists.Exists) continue;

            var response = await client.Indices.CreateAsync(index, c => c
                .Mappings(m => m.Properties<ProductSearchDocument>(p => p
                    .Keyword(d => d.Id)
                    .Keyword(d => d.Culture)
                    .Text(d => d.Name, t => t.Analyzer(analyzer))
                    .Text(d => d.Description!, t => t.Analyzer(analyzer))
                    .Text(d => d.Brand!, t => t.Fields(f => f.Keyword("raw")))
                    .Keyword(d => d.Category!)
                    .Text(d => d.AttributesText!, t => t.Analyzer(analyzer))
                    .Keyword(d => d.Slug)
                    .Keyword(d => d.ImageId!)
                    .DoubleNumber(d => d.PriceAmount)
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

public sealed class ElasticsearchProductIndexer(ElasticsearchClient client) : IProductIndexer
{
    public async Task IndexAsync(Product product, CancellationToken ct = default)
    {
        foreach (var culture in SearchCultures.Analyzers.Keys)
        {
            var document = ProductSearchDocument.FromProduct(product, culture);
            var response = await client.IndexAsync(
                document,
                i => i.Index(ProductSearchDocument.IndexNameFor(culture)).Id(document.Id),
                ct);

            if (!response.IsValidResponse)
                throw new InvalidOperationException(
                    $"Indexing product {document.Id} into {culture} failed: {response.DebugInformation}");
        }
    }

    public async Task RemoveAsync(ProductId productId, CancellationToken ct = default)
    {
        foreach (var culture in SearchCultures.Analyzers.Keys)
            await client.DeleteAsync<ProductSearchDocument>(
                productId.ToString(),
                d => d.Index(ProductSearchDocument.IndexNameFor(culture)),
                ct); // 404 aquí es aceptable: borrar lo no indexado es idempotente
    }
}

public sealed class ElasticsearchLexicalSearch(ElasticsearchClient client) : ILexicalProductSearch
{
    private static readonly ActivitySource Telemetry = new("Tendero.Search");

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
            h.Source!.Id,
            h.Source.Name,
            h.Source.Slug,
            h.Source.Brand,
            h.Source.Category,
            h.Source.PriceAmount,
            h.Source.PriceCurrency,
            h.Source.ImageId,
            h.Score ?? 0d)).ToList();

        activity?.SetTag("search.total", response.Total);
        activity?.SetTag("search.took_ms", response.Took);

        return new SearchResultPage(hits, response.Total, query.Page, query.PageSize, response.Took);
    }
}
