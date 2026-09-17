using System.Diagnostics;
using System.Text.RegularExpressions;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
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

            await SearchIndexes.CreateAsync(client, culture, analyzer, cancellationToken);
            logger.LogInformation("Search index {Index} created with analyzer {Analyzer}", index, analyzer);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The index mapping, in one place.
///
/// It was inline in the startup initializer, which was fine while creating was
/// the only thing anybody did. It is not: an index that already exists is never
/// re-mapped, so adding a field to the document leaves the live index mapping it
/// dynamically — and a keyword array comes back as `text`, which a term filter
/// silently fails to match. Recreating has to use the SAME mapping as creating,
/// and two copies of it would agree right up until somebody edited one.
/// </summary>
internal static class SearchIndexes
{
    public static async Task CreateAsync(
        ElasticsearchClient client, string culture, string analyzer, CancellationToken ct)
    {
        var index = ProductSearchDocument.IndexNameFor(culture);

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
                // The branch, for filtering a category page. A keyword array so
                // a term filter on "KITCHEN" reaches everything below it.
                .Keyword(d => d.CategoryCodes)
                .Date(d => d.AvailableFrom!)
                .Text(d => d.CategoryPathText!, t => t.Analyzer(analyzer))
                .Text(d => d.AttributesText!, t => t.Analyzer(analyzer))
                .Keyword(d => d.Slug)
                .Keyword(d => d.Code)
                .Keyword(d => d.ImageId!)
                .Boolean(d => d.InStock)
                .DoubleNumber(d => d.PriceAmount)
                .DoubleNumber(d => d.PriceFrom)
                .DoubleNumber(d => d.PriceTo)
                .Keyword(d => d.PriceCurrency)
                .Keyword(d => d.Status))),
            ct);

        if (!response.IsValidResponse)
            throw new InvalidOperationException(
                $"Could not create index '{index}': {response.DebugInformation}");
    }
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
    ICategoryReader categories,
    IAvailabilityReader availability) : IProductIndexer
{
    public async Task RecreateAsync(CancellationToken ct = default)
    {
        foreach (var (culture, analyzer) in CultureAnalyzers.ByCulture)
        {
            var index = ProductSearchDocument.IndexNameFor(culture);

            // Delete then create, and the delete is allowed to find nothing: the
            // point is to end with an index whose mapping matches the document,
            // not to prove one was there.
            await client.Indices.DeleteAsync(index, ct);
            await SearchIndexes.CreateAsync(client, culture, analyzer, ct);
        }
    }

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        foreach (var culture in CultureAnalyzers.ByCulture.Keys)
        {
            var index = ProductSearchDocument.IndexNameFor(culture);
            var response = await client.CountAsync(c => c.Indices(index), ct);

            // A missing index is as empty as an index can be, and Elasticsearch
            // answers 404 for it rather than a count of zero.
            if (response.ApiCallDetails.HttpStatusCode == 404)
                return true;

            if (!response.IsValidResponse)
                throw new SearchUnavailableException("count", response.DebugInformation);

            if (response.Count == 0)
                return true;
        }

        return false;
    }

    public async Task IndexAsync(Product product, CancellationToken ct = default)
    {
        var definitions = await attributeDefinitions.AllAsync(ct);
        var tree = await categories.AllAsync(ct);

        // One question for the whole product, not one per variant per culture:
        // a product with eight variants would otherwise be sixteen round trips
        // to answer a boolean.
        var available = await availability.AvailableAsync(
            [.. product.Variants.Select(variant => variant.Sku)], ct);

        foreach (var culture in SearchCultures.Supported)
        {
            // WRITE FIRST, THEN SWEEP. Reindexing a product means replacing all
            // of its variants — a retired one has to disappear, and writing only
            // the live ones would leave it there forever — but doing the delete
            // first was wrong twice over.
            //
            // It opened a window in which a published product was not in the
            // index at all. And it deadlocked against itself: `delete_by_query`
            // takes a snapshot and aborts with a 409 if a document's version
            // moved underneath it, which is exactly what happens when the same
            // product is projected twice in quick succession. Phase 4 made that
            // the normal case — `ProductUpserted` and `StockLevelChanged` both
            // rebuild the whole document — and it dead-lettered ten messages
            // with `inStock` stuck false on a shop that had stock.
            //
            // Indexing by document id is an upsert, so writing first is safe and
            // the sweep afterwards only ever has to remove ids that are NOT in
            // this write. That is also why `conflicts: proceed` is honest here:
            // a document being rewritten concurrently is by definition a current
            // one, and current ones are excluded from the sweep.
            var written = new List<string>();

            foreach (var document in ProductSearchDocument.ForVariants(
                         product, culture, definitions, tree, available))
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

                written.Add(document.Id);
            }

            await SweepAsync(product.Id, culture, written, ct);
        }
    }

    public async Task RemoveAsync(ProductId productId, CancellationToken ct = default)
    {
        foreach (var culture in SearchCultures.Supported)
            await SweepAsync(productId, culture, [], ct);
    }

    /// <summary>
    /// Deletes by QUERY and not by id: the document is no longer the product but
    /// each of its variants, and how many there are is not known from here.
    ///
    /// <paramref name="keep"/> is the set of document ids just written. Passing
    /// none removes the product entirely, which is what archiving does; passing
    /// the current variants turns it into the sweep half of a reindex.
    /// </summary>
    private async Task SweepAsync(
        ProductId productId, string culture, IReadOnlyCollection<string> keep, CancellationToken ct)
    {
        var response = await client.DeleteByQueryAsync<ProductSearchDocument>(
            ProductSearchDocument.IndexNameFor(culture),
            d => d
                .Query(q => q.Bool(b =>
                {
                    b.Filter(f => f.Term(t => t.Field(field => field.ProductId).Value(productId.ToString())));

                    if (keep.Count > 0)
                        b.MustNot(m => m.Ids(i => i.Values(new Ids([.. keep]))));
                }))
                // The sweep never touches a document this call just wrote, so a
                // version that moved underneath the snapshot belongs to another
                // projection of the SAME product — which will write it correctly
                // and sweep after itself. Aborting there is how ten messages
                // dead-lettered; proceeding loses nothing.
                .Conflicts(Conflicts.Proceed),
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

internal sealed partial class ElasticsearchLexicalSearch(ElasticsearchClient client) : ILexicalProductSearch
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

    /// <summary>
    /// The fields that say what a product IS, without boosts: the full-match count
    /// scores nothing. The description is left out on purpose (ADR 0032).
    /// </summary>
    internal static readonly string[] IdentityFields = ["name", "brand", "attributesText", "categoryPathText"];

    /// <summary>
    /// One <c>best_fields</c> clause per WORD of the query, every word required
    /// (ADR 0032, fix round 1) — not one <c>cross_fields</c> clause over the whole
    /// query text.
    ///
    /// <c>brand</c> is mapped with no analyser (it keeps a <c>raw</c> keyword
    /// subfield for exact filtering, in <c>SearchIndexes.CreateAsync</c>), while
    /// <c>name</c>, <c>attributesText</c> and <c>categoryPathText</c> use the
    /// culture analyser. <c>cross_fields</c> only blends fields that share an
    /// analyser, so a single cross_fields/AND clause over
    /// <see cref="IdentityFields"/> silently splits into two independent groups —
    /// <c>{name, attributesText, categoryPathText}</c> and <c>{brand}</c> — and
    /// each group then has to hold every word alone. "Kuisina cafetera" needs
    /// "Kuisina" in the group that has no brand field and "cafetera" in the group
    /// that has no brand field, so it could never pass. Asking "does EVERY WORD
    /// appear in AT LEAST ONE identity field" sidesteps the grouping: each word
    /// gets its own <c>best_fields</c> clause across every field, analysed its own
    /// way, and the clauses are ANDed by putting them all in one
    /// <c>.Must(...)</c> call.
    ///
    /// <see cref="ZeroTermsQuery.All"/> matters for the same reason: with one
    /// clause per word, a word the culture analyser reduces to nothing — a
    /// stopword such as "para", "de" or "for" — would otherwise make ITS clause
    /// match nothing (the default is <see cref="ZeroTermsQuery.None"/>), and one
    /// impossible clause fails the whole AND. "cafetera para la cocina" should not
    /// be refused because "para" and "la" analyse away; <c>All</c> treats a word
    /// with nothing left after analysis as satisfied by anything, which is what a
    /// stopword deserves.
    /// </summary>
    internal static Action<QueryDescriptor<ProductSearchDocument>>[] AllWordsInWhatItIs(string text)
    {
        var words = Words(text);
        var asked = words.Where(word => !IsBareMeasurement(word)).ToArray();

        // Every word was a measurement, so there is nothing left to ask. An empty
        // `must` matches everything, which would silence the notice on exactly those
        // queries, so the whole query is asked instead.
        return [.. (asked.Length > 0 ? asked : words).Select(WordInAnyIdentityField)];
    }

    /// <summary>
    /// A number with at most a short unit glued to it — "6l", "24cm", "1,5l", "15" —
    /// and nothing else. Measured on 2026-09-17 (ADR 0032, fix round 2): the catalogue
    /// writes the unit apart ("6 litros", "sartén de 24 cm") and shoppers write it
    /// joined, so the token matches nothing and the notice said "nothing matches
    /// exactly" above the right pan. Six of the fifty-five notices were this, every one
    /// on a page answered at 0.634 or better, and skipping them silenced nothing honest.
    ///
    /// It is skipped in the full-match check ONLY. The check asks whether the shop
    /// carries this KIND of thing; the size is the search's business, and
    /// <see cref="SearchAsync"/> still ranks with the whole query.
    ///
    /// What it costs: the notice goes blind to a SIZE mismatch — a shop holding only a
    /// 4 L pot, asked for a 6 L one, no longer admits it on the litres. At a hundred
    /// products, where sizes are a closed attribute, that is the cheaper error. The
    /// version that does not pay it normalises "24cm" into "24 cm" at index and query
    /// time, which is an analyser change and a full reindex.
    ///
    /// The same shape also swallows a feature written as digits plus a short suffix —
    /// "4k", "1080p", "3d", "5g" — and a bare year. Nothing in this catalogue is sold
    /// by any of those, so the false skip costs a size or spec mismatch going
    /// unnoticed, never a wrong answer.
    /// </summary>
    private static bool IsBareMeasurement(string word) => BareMeasurement().IsMatch(word);

    // Both cases spelled out rather than asking for IgnoreCase: `RegexOptions` is
    // ambiguous in this file, since Elasticsearch has one of its own.
    [GeneratedRegex(@"^\d+([.,]\d+)?[A-Za-z]{0,3}$")]
    private static partial Regex BareMeasurement();

    private static Action<QueryDescriptor<ProductSearchDocument>> WordInAnyIdentityField(string word) =>
        must => must.MultiMatch(mm => mm
            .Query(word)
            .Fields(IdentityFields)
            .Type(TextQueryType.BestFields)
            .Operator(Operator.And)
            .ZeroTermsQuery(ZeroTermsQuery.All));

    /// <summary>Splits on whitespace, trimmed, empties dropped — a null separator
    /// is <see cref="string.Split(char[]?, StringSplitOptions)"/>'s own way of
    /// asking for "any whitespace".</summary>
    private static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

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
            // Sorting is opt-in and there is exactly one option, because a sort
            // is a promise the index has to be able to keep. Relevance stays the
            // default: it is the only order that means anything when somebody
            // typed something, and the golden set measures it.
            .Sort(sort =>
            {
                if (!string.Equals(query.Sort, "newest", StringComparison.OrdinalIgnoreCase))
                    return;

                sort.Field(d => d.AvailableFrom!, f => f.Order(SortOrder.Desc));
            })
            // The `hits` total counts DOCUMENTS, and here a document is a
            // variant. The number the interface shows is products, so it comes
            // from a cardinality over the collapsed field. Approximate above
            // 40,000 groups, exact far below that.
            .Aggregations(a => a.Add("products", agg => agg.Cardinality(c => c.Field(d => d.ProductId))))
            .Query(q => q.Bool(b => b
                // **An empty query is a BROWSE, not a failure.** Everything a
                // home page shows — a category, what is new, what is on offer —
                // is this question with no words in it, and until there was a
                // home page nobody noticed the only way in was to already know
                // what you wanted.
                //
                // `MinimumShouldMatch` is what makes it safe: with no `should`
                // clauses the boolean matches everything, and the filters below
                // still apply.
                .Must(m =>
                {
                    // `Must` takes an action rather than returning a clause, so
                    // this is an if and not the ternary it wants to be.
                    if (string.IsNullOrWhiteSpace(query.Text))
                    {
                        m.MatchAll(_ => { });
                        return;
                    }

                    m.Bool(alternatives => alternatives
                        .Should(
                            AllWordsAcrossFields(query.Text),
                            AllWordsForgivingTypos(query.Text),
                            // **The fallback, and most of what recall was
                            // missing.** With only AND clauses a query of three
                            // words returned the one product carrying all three:
                            // "tech tee" found 1 of 5 relevant products, "running
                            // shoes" 2 of 8. The docs blamed a vocabulary gap for
                            // hybrid search to earn; measured on 2026-09-11 over a
                            // golden set re-pooled so nothing in anybody's top ten
                            // was unjudged, this one clause took recall@50 from
                            // 0.564 to 0.939 (es) and 0.457 to 0.955 (en), and
                            // NDCG@10 from 0.800 to 0.927 and 0.739 to 0.935. The
                            // AND clause's boost keeps full matches first; this
                            // only decides what comes after them.
                            should => should.MultiMatch(mm => mm
                                .Query(query.Text)
                                .Fields(SearchableFields)
                                .Type(TextQueryType.CrossFields)
                                .Operator(Operator.Or)))
                        .MinimumShouldMatch(1));
                })
                .Filter(ActiveInBranch(query.Category)))),
            ct);

        if (!response.IsValidResponse)
            throw new SearchUnavailableException("query", response.DebugInformation);

        var hits = response.Hits.Select(SearchHits.From).ToList();

        var products = response.Aggregations?.GetCardinality("products")?.Value is { } value
            ? (long)value
            : hits.Count;

        activity?.SetTag("search.total", products);
        activity?.SetTag("search.variants", response.Total);
        activity?.SetTag("search.took_ms", response.Took);

        return new SearchResultPage(hits, products, query.Page, query.PageSize, response.Took);
    }

    /// <summary>
    /// cross_fields with every word required: the terms may spread across fields.
    /// "zapatillas running mujer" has the first two in name and the third in
    /// attributesText; with best_fields none matched, because it demanded all of
    /// them in ONE field. Boosted, so a product carrying EVERY word still outranks
    /// one carrying some.
    /// </summary>
    internal static Action<QueryDescriptor<ProductSearchDocument>> AllWordsAcrossFields(string text) =>
        should => should.MultiMatch(mm => mm
            .Query(text)
            .Fields(SearchableFields)
            .Type(TextQueryType.CrossFields)
            .Operator(Operator.And)
            .Boost(2));

    /// <summary>
    /// best_fields with fuzziness and every word required: it tolerates typos
    /// ("zapatilas"). Kept apart because cross_fields does NOT support fuzziness.
    /// </summary>
    internal static Action<QueryDescriptor<ProductSearchDocument>> AllWordsForgivingTypos(string text) =>
        should => should.MultiMatch(mm => mm
            .Query(text)
            .Fields(SearchableFields)
            .Fuzziness(new Fuzziness("AUTO"))
            .Operator(Operator.And));

    /// <summary>
    /// Active, and inside the branch when there is one — as separate filters, passed
    /// to ONE <c>.Filter(...)</c> call.
    ///
    /// **In this client a second <c>.Filter(...)</c> on a bool query, or a second
    /// <c>.Term(...)</c> on one query descriptor, replaces the first instead of
    /// adding to it.** It compiles and says nothing. Until 2026-09-15 this method
    /// called <c>.Term</c> twice, so a category browse sent only the category and
    /// showed products that were not Active; <c>ElasticsearchRequestBodyTests</c>
    /// reads the body the client really writes.
    /// </summary>
    internal static Action<QueryDescriptor<ProductSearchDocument>>[] ActiveInBranch(string? category) =>
        string.IsNullOrWhiteSpace(category)
            ? [Active]
            // The branch, not the leaf: `CategoryCodes` holds the whole ancestry, so
            // one term reaches everything under it.
            : [Active, filter => filter.Term(t => t.Field(d => d.CategoryCodes).Value(category))];

    private static readonly Action<QueryDescriptor<ProductSearchDocument>> Active =
        filter => filter.Term(t => t.Field(d => d.Status).Value("active"));
}

/// <summary>
/// Whether a query has a full match — whether the shop carries something described
/// by every word, not whether a lexical search would find something for it.
///
/// It no longer mirrors the search query. At a hundred products, scoring every
/// word across every field including the description is harmless; at a million,
/// some description contains the words of almost any query
/// ("ideal para regalar a tu madre"), and a fuzzy match turns "movil" into "mobil"
/// — either would make the notice vanish exactly where it is needed (spec D5). The
/// count reads only the fields that say what a product IS
/// (<see cref="ElasticsearchLexicalSearch.IdentityFields"/>) and forgives no typos.
/// A count, not a search: nothing is scored or fetched.
/// </summary>
internal sealed class ElasticsearchFullMatch(ElasticsearchClient client) : ILexicalFullMatch
{
    public async Task<bool> AnyAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
            return false;

        var response = await client.CountAsync<ProductSearchDocument>(c => c
            .Indices(ProductSearchDocument.IndexNameFor(query.Culture))
            .Query(q => q.Bool(b => b
                .Must(ElasticsearchLexicalSearch.AllWordsInWhatItIs(query.Text))
                .Filter(ElasticsearchLexicalSearch.ActiveInBranch(query.Category)))),
            ct);

        if (!response.IsValidResponse)
            throw new SearchUnavailableException("counting full matches", response.DebugInformation);

        return response.Count > 0;
    }
}

/// <summary>
/// The cards for products the vector side found (P8-5), from the same documents
/// the lexical search reads — so a meaning-only hit shows the same price, image
/// and stock a lexical hit for that product would.
/// </summary>
internal sealed class ElasticsearchHitReader(ElasticsearchClient client) : ISearchHitReader
{
    public async Task<IReadOnlyList<SearchHit>> FindAsync(
        IReadOnlyCollection<string> productIds, string culture, string? category, CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return [];

        var response = await client.SearchAsync<ProductSearchDocument>(s => s
            .Indices(ProductSearchDocument.IndexNameFor(culture))
            .Size(productIds.Count)
            .Collapse(c => c.Field(d => d.ProductId))
            // No words chose a variant, so the card shows the one somebody can buy,
            // cheapest first — the choice a person browsing would make.
            .Sort(
                sort => sort.Field(d => d.InStock, f => f.Order(SortOrder.Desc)),
                sort => sort.Field(d => d.PriceAmount, f => f.Order(SortOrder.Asc)))
            // Every filter in ONE `.Filter(...)` call: a second call replaces the
            // first. Until 2026-09-15 this query lost its id filter to the two after
            // it and answered with the cheapest Active products, so hybrid search
            // kept only the meaning-found products that turned up by luck.
            //
            // The branch filter is the one the lexical query applies, so a vector
            // that disagrees with the index about the category loses.
            .Query(q => q.Bool(b => b
                .Filter(
                [
                    filter => filter.Terms(t => t
                        .Field(d => d.ProductId)
                        .Terms(new TermsQueryField([.. productIds.Select(id => FieldValue.String(id))]))),
                    .. ElasticsearchLexicalSearch.ActiveInBranch(category)
                ]))),
            ct);

        if (!response.IsValidResponse)
            throw new SearchUnavailableException("reading cards for vector hits", response.DebugInformation);

        return [.. response.Hits.Select(SearchHits.From)];
    }
}

/// <summary>A document hit as the search contract's <see cref="SearchHit"/>, in one place for both readers.</summary>
internal static class SearchHits
{
    public static SearchHit From(Hit<ProductSearchDocument> hit) => new(
        hit.Source!.ProductId,
        hit.Source.Code,
        hit.Source.Name,
        hit.Source.Slug,
        hit.Source.Brand,
        hit.Source.Category,
        hit.Source.Id,
        hit.Source.Sku,
        hit.Source.PriceAmount,
        hit.Source.PriceFrom,
        hit.Source.PriceTo,
        hit.Source.PriceCurrency,
        hit.Source.ImageId,
        hit.Source.InStock,
        hit.Score ?? 0d);
}
