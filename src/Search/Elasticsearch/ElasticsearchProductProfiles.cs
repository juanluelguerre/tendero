using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Offers;

namespace ElGuerre.Tendero.Search.Elasticsearch;

/// <summary>
/// Products read whole from the culture's index (R-8): every variant document of
/// each product asked for, grouped into a <see cref="ProductProfile"/> by the pure
/// rule beside the record — Active only, as every reader of the index filters.
///
/// **Every variant, not a collapsed hit.** A collapse keeps one variant, and a
/// profile needs all of them: sold out is no variant in stock, and a product's
/// colours are its variants' colours. At laboratory scale a category holds a few
/// dozen documents; <see cref="MaxDocuments"/> bounds a request, and a category
/// that outgrew it would move the grouping into an aggregation — written down
/// here with its number rather than built for a volume the shop does not have.
/// </summary>
internal sealed class ElasticsearchProductProfiles(ElasticsearchClient client, IRunningOffers offers) : IProductProfileReader
{
    /// <summary>
    /// Variant documents one request may read: the largest category of the
    /// laboratory's hundred products is thirteen shoes of up to eight variants
    /// each, so eight categories' neighbourhoods fit several times over.
    /// </summary>
    internal const int MaxDocuments = 1000;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public Task<IReadOnlyList<ProductProfile>> ByIdsAsync(IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
        ReadAsync("ids", d => d.ProductId, productIds, culture, ct);

    public Task<IReadOnlyList<ProductProfile>> ByCodesAsync(IReadOnlyCollection<string> codes, string culture, CancellationToken ct = default) =>
        ReadAsync("codes", d => d.Code, codes, culture, ct);

    public Task<IReadOnlyList<ProductProfile>> InCategoriesAsync(IReadOnlyCollection<string> categories, string culture, CancellationToken ct = default) =>
        ReadAsync("categories", d => d.Category, categories, culture, ct);

    private async Task<IReadOnlyList<ProductProfile>> ReadAsync(
        string by, System.Linq.Expressions.Expression<Func<ProductSearchDocument, object?>> field,
        IReadOnlyCollection<string> values, string culture, CancellationToken ct)
    {
        if (values.Count == 0)
            return [];

        using var activity = Telemetry.StartActivity("search.profiles");
        activity?.SetTag("search.profiles.by", by);
        activity?.SetTag("search.profiles.asked", values.Count);

        var response = await client.SearchAsync<ProductSearchDocument>(s => s
            .Indices(ProductSearchDocument.IndexNameFor(culture))
            .Size(MaxDocuments)
            // Every filter in ONE `.Filter(...)` call: a second call replaces the
            // first (the hit reader's lesson, 2026-09-15).
            .Query(q => q.Bool(b => b
                .Filter(
                [
                    filter => filter.Terms(t => t
                        .Field(field)
                        .Terms(new TermsQueryField([.. values.Select(value => FieldValue.String(value))]))),
                    .. ElasticsearchLexicalSearch.ActiveInBranch(category: null)
                ]))),
            ct);

        if (!response.IsValidResponse)
            throw new SearchUnavailableException($"reading product profiles by {by}", response.DebugInformation);

        var profiles = ProductProfile.Of(response.Documents, await offers.DealsNowAsync(culture, ct));
        activity?.SetTag("search.profiles.found", profiles.Count);

        return profiles;
    }
}
