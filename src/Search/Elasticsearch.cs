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
/// Which native Elasticsearch analyser each culture uses. An engine detail, and
/// internal for that reason: outside here what is known is
/// <see cref="SearchCultures.Supported"/>, not that a stemmer called "spanish"
/// exists.
/// </summary>
internal static class CultureAnalyzers
{
    public static readonly IReadOnlyDictionary<string, string> ByCulture = new Dictionary<string, string>
    {
        ["es"] = "spanish",   // native ES analysers: stemming + stopwords
        ["en"] = "english"
    };
}

/// <summary>Creates products_es and products_en at startup if they are missing (idempotent).</summary>
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
                    // The collapse field: it has to be a keyword, not text.
                    .Keyword(d => d.ProductId)
                    .Keyword(d => d.Sku)
                    .Keyword(d => d.AxisValues)
                    .Keyword(d => d.Culture)
                    .Text(d => d.Name, t => t.Analyzer(analyzer))
                    .Text(d => d.Description!, t => t.Analyzer(analyzer))
                    .Text(d => d.Brand!, t => t.Fields(f => f.Keyword("raw")))
                    .Keyword(d => d.Category!)
                    .Text(d => d.CategoryPathText!, t => t.Analyzer(analyzer))
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
/// Builds the document and writes it. It needs the attribute definitions because
/// the searchable text is rendered IN THE INDEX'S CULTURE: without them,
/// products_en would contain "color azul marino" and no English query could match
/// it — which is exactly why "navy blue shoes" scores 0.000 against the committed
/// baseline.
/// </summary>
internal sealed class ElasticsearchProductIndexer(
    ElasticsearchClient client,
    IAttributeDefinitionReader attributeDefinitions,
    ICategoryReader categories) : IProductIndexer
{
    public async Task IndexAsync(Product product, CancellationToken ct = default)
    {
        var definitions = await attributeDefinitions.AllAsync(ct);
        var tree = await categories.AllAsync(ct);

        foreach (var culture in SearchCultures.Supported)
        {
            // Reindexing a product means replacing ALL of its variants, not
            // adding: if one is retired its document has to disappear, and
            // writing only the live ones would leave the retired one there forever.
            await RemoveAsync(product.Id, culture, ct);

            foreach (var document in ProductSearchDocument.ForVariants(product, culture, definitions, tree))
            {
                var response = await client.IndexAsync(
                    document,
                    i => i.Index(ProductSearchDocument.IndexNameFor(culture)).Id(document.Id),
                    ct);

                // SearchUnavailableException and not InvalidOperationException:
                // an engine failure is a retryable 503. It came out as a 500 down
                // this path and as a 503 down the query one, for the same fault.
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
    /// Deletes by QUERY and not by id: the document is no longer the product but
    /// each of its variants, and how many there are is not known from here.
    /// </summary>
    private async Task RemoveAsync(ProductId productId, string culture, CancellationToken ct)
    {
        {
            var response = await client.DeleteByQueryAsync<ProductSearchDocument>(
                ProductSearchDocument.IndexNameFor(culture),
                d => d.Query(q => q.Term(t => t.Field(f => f.ProductId).Value(productId.ToString()))),
                ct);

            // Deleting what is not indexed is idempotent, and it is the normal
            // case for a product that was never published: delete_by_query
            // returns zero deletions, not an error. A real failure cannot be
            // ignored — this is the path Archive() takes a product out of the
            // catalogue by, and swallowing a 503 would leave the retired one indexed.
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
    /// The text fields with their boosts. `category` is NOT here: it is mapped as
    /// a keyword and would only match the exact term ("COFFEE_MAKER"), so listing
    /// it among text fields promised a search that never happened.
    ///
    /// What is here instead is `categoryPathText`, which is the SAME taxonomy
    /// turned into user-facing text and analysed — the whole branch, so whoever
    /// searches "cocina" finds what is inside it.
    /// </summary>
    private static readonly string[] SearchableFields =
        ["name^3", "brand^2", "attributesText^2", "categoryPathText^2", "description"];

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        using var activity = Telemetry.StartActivity("search.lexical");
        activity?.SetTag("search.culture", query.Culture);
        activity?.SetTag("search.text", query.Text);

        var response = await client.SearchAsync<ProductSearchDocument>(s => s
            .Indices(ProductSearchDocument.IndexNameFor(query.Culture))
            .From((query.Page - 1) * query.PageSize)
            .Size(query.PageSize)
            // Collapse by product: matching and filtering happen per variant —
            // which is what makes them exact — and the result comes back as a
            // product, with the variant that won inside it.
            .Collapse(c => c.Field(d => d.ProductId))
            // The `hits` total counts DOCUMENTS, and here a document is a
            // variant. The number the interface shows is products, so it comes
            // from a cardinality over the collapsed field. Approximate above
            // 40,000 groups, exact far below that.
            .Aggregations(a => a.Add("products", agg => agg.Cardinality(c => c.Field(d => d.ProductId))))
            .Query(q => q.Bool(b => b
                // Two ways of matching the same query, joined by should. Each
                // covers what the other cannot, and that is NOT decoration: the
                // golden set measures both (see docs/search-evaluation.md).
                .Must(m => m.Bool(alternatives => alternatives
                    .Should(
                        // cross_fields: the terms may spread across fields.
                        // "zapatillas running mujer" has the first two in name and
                        // the third in attributesText; with best_fields none of
                        // them matched, because it demanded all of them in ONE field.
                        should => should.MultiMatch(mm => mm
                            .Query(query.Text)
                            .Fields(SearchableFields)
                            .Type(TextQueryType.CrossFields)
                            .Operator(Operator.And)),
                        // best_fields with fuzziness: it tolerates typos ("zapatilas").
                        // Kept apart because cross_fields does NOT support fuzziness.
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
