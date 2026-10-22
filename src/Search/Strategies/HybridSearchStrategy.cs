using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Strategies;

/// <summary>BM25 alone, through the port that does not change (ADR 0004).</summary>
internal sealed class LexicalSearchStrategy(ILexicalProductSearch lexical) : ISearchStrategy
{
    public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default) =>
        lexical.SearchAsync(query, ct);
}

/// <summary>The knobs of hybrid search, bound from <c>Search:Hybrid</c>. The defaults are the measured ones.</summary>
public sealed record HybridSearchOptions
{
    public const string Section = "Search:Hybrid";

    /// <summary>How many lexical products are fused. Page one and then some: fusion cannot promote what was never fetched.</summary>
    public int LexicalDepth { get; init; } = 50;

    /// <summary>How many nearest products the vector side contributes.</summary>
    public int VectorDepth { get; init; } = 20;

    public int RrfK { get; init; } = ReciprocalRankFusion.DefaultK;

    /// <summary>How many fused results the reranker re-sorts.</summary>
    public int RerankDepth { get; init; } = 20;

    /// <summary>Embedding the query and asking Qdrant, together. Past it, the answer is lexical.</summary>
    public TimeSpan VectorBudget { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Reading the texts and scoring them, together. Past it, the fused order stands.</summary>
    public TimeSpan RerankBudget { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// How many products the PICTURES contribute: the query embedded as text by the
    /// picture model, the nearest catalogue pictures, each product once (phase 13a).
    /// **Zero, which is off, until it is measured to help** — the images spec keeps
    /// text against pictures out of the search until its effect on NDCG@10 is known,
    /// and `docs/search-evaluation.md` has what was measured.
    /// </summary>
    public int PictureDepth { get; init; }

    /// <summary>Embedding the query for the pictures and asking for the nearest, together. Past it, the search goes on without them.</summary>
    public TimeSpan PictureBudget { get; init; } = TimeSpan.FromMilliseconds(600);
}

/// <summary>
/// Hybrid search (P8-5…P8-7): lexical and vector rankings fused by rank, the head
/// optionally reranked, and **every AI stage allowed to fail**.
///
/// The degradation is staged rather than all-or-nothing, and each stage fails
/// towards the one before it:
///
/// <list type="bullet">
/// <item>The vector side fails or runs out of its budget → the answer is the
/// lexical strategy's own answer to the ORIGINAL query, byte for byte. Not the
/// lexical candidates re-paged, which would be a different page with a different
/// total — the test that holds this compares serialised JSON (P8-8).</item>
/// <item>The reranker fails or runs out of its budget → the fused order stands.</item>
/// </list>
///
/// Both tag the span <c>search.degraded</c> with the stage and add to the
/// <c>tendero.search.degraded</c> counter, because a silent fallback is
/// indistinguishable from bad relevance (P8-7).
///
/// **Not configured is a third thing, and it is not a degradation.** A process
/// this deployment never gave a vector layer or a reranker
/// (<see cref="SearchStageNotConfiguredException"/>) skips the stage and tags the
/// span <c>search.stage.unavailable</c> with which one — but is never counted and
/// never logged, because a deployment that never carried the stage is not the
/// day the stage went down (ADR 0023 amendment). Reranking in particular is no
/// longer a flag: it runs whenever <c>reranker.IsConfigured</c> says a real one
/// was registered, which mirrors the AppHost's own decision to start the
/// reranker container only where it can keep its budget — a GPU today.
///
/// A browse — no words — and a date order are answered by lexical alone: there is
/// no meaning to add to "everything in the kitchen", and fusing relevance into
/// "newest first" would break the order the shopper asked for.
/// </summary>
internal sealed class HybridSearchStrategy(
    ILexicalProductSearch lexical,
    IVectorProductSearch vectors,
    ISearchHitReader cards,
    IReranker reranker,
    IImageEmbeddingGenerator pictureModel,
    IPictureSearch pictures,
    IOptions<HybridSearchOptions> options,
    ILogger<HybridSearchStrategy> logger) : ISearchStrategy
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        if (String.IsNullOrWhiteSpace(query.Text) || query.Sort is not ProductSort.Relevance)
            return await lexical.SearchAsync(query, ct);

        using var activity = Telemetry.StartActivity("search.hybrid");
        activity?.SetTag("search.culture", query.Culture);
        activity?.SetTag("search.text", query.Text);

        var settings = options.Value;
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<VectorMatch> matches;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(settings.VectorBudget);

            matches = await vectors.SearchAsync(query.Text, query.Culture, query.Category, settings.VectorDepth, budget.Token);
        }
        catch (SearchStageNotConfiguredException missing)
        {
            activity?.SetTag(SearchDegradation.NotConfiguredTag, missing.Stage);
            return await lexical.SearchAsync(query, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            SearchDegradation.Record(activity, SearchDegradation.VectorStage, exception, logger);
            return await lexical.SearchAsync(query, ct);
        }

        var depth = Math.Max(settings.LexicalDepth, query.Page * query.PageSize);
        var found = matches.Select(match => match.ProductId).ToList();
        var looks = await this.LooksAsync(query, settings, activity, ct);

        // **The union** (the facets spec, "The API"). A page that lists what the
        // words matched AND what meaning found has to count both, so the lexical
        // request carries the vector half's ids as a second way to match: its
        // total and every facet are over the union, narrowed by the filters. The
        // ids score nothing, so the words' ranking is the one it always was, and
        // the products only meaning found come after them with a score of zero.
        var candidates = await lexical.SearchAsync(
            query with { Page = 1, PageSize = depth, AlsoMatching = looks.Count == 0 ? found : [.. found.Union(looks, StringComparer.Ordinal)] }, ct);

        // The words' own ranking is what fuses: a product in the union only
        // because meaning found it is not a lexical match, and ranking it as one
        // would count meaning twice.
        var matchedByWords = candidates.Hits.Where(hit => hit.Score > 0).ToList();
        var words = matchedByWords.Select(hit => hit.ProductId).ToList();

        var byProduct = new Dictionary<string, SearchHit>(StringComparer.Ordinal);
        foreach (var hit in matchedByWords)
            byProduct.TryAdd(hit.ProductId, hit);

        var filters = query.Filters ?? SearchFilters.None;
        var foundByMeaningOnly = found.Concat(looks).Distinct(StringComparer.Ordinal).Where(id => !byProduct.ContainsKey(id)).ToList();
        if (foundByMeaningOnly.Count > 0)
        {
            foreach (var hit in await cards.FindAsync(foundByMeaningOnly, query.Culture, query.Category, filters, ct))
                byProduct.TryAdd(hit.ProductId, hit);
        }

        // **Qdrant knows nothing of brands, prices or sizes**, so its nearest list
        // is unfiltered. With filters it is cut to the products that pass them
        // BEFORE fusion — a product the filters exclude must not take a rank
        // another one would have had. Without filters it is fused whole, exactly
        // as before they existed.
        var nearest = filters.IsEmpty ? found : [.. found.Where(byProduct.ContainsKey)];

        // The pictures' list by the same rule, and only when there is one: with
        // none, the fusion is the two lists it always was, rank for rank.
        var seen = filters.IsEmpty ? looks : [.. looks.Where(byProduct.ContainsKey)];
        var fused = seen.Count == 0
            ? ReciprocalRankFusion.Fuse([words, nearest], settings.RrfK)
            : ReciprocalRankFusion.Fuse([words, nearest, seen], settings.RrfK);

        // An id with no card is a vector nobody can buy — archived, out of the
        // branch, or left behind by a projection that failed — and it is dropped.
        var ranked = fused
            .Where(item => byProduct.ContainsKey(item.Id))
            .Select(item => byProduct[item.Id] with { Score = item.Score })
            .ToList();

        activity?.SetTag("search.hybrid.lexical", words.Count);
        activity?.SetTag("search.hybrid.vector", nearest.Count);
        activity?.SetTag("search.hybrid.pictures", seen.Count);
        activity?.SetTag("search.hybrid.meaning_only", ranked.Count(hit => foundByMeaningOnly.Contains(hit.ProductId)));

        if (reranker.IsConfigured)
            ranked = await this.RerankAsync(query, ranked, settings, activity, ct);
        else
            activity?.SetTag(SearchDegradation.NotConfiguredTag, SearchDegradation.RerankStage);

        // Exact when lexical returned everything it matched, which is the case at
        // a hundred products; otherwise the lexical count — the union's, so it
        // already holds what meaning found.
        var total = candidates.Total <= candidates.Hits.Count
            ? ranked.Count
            : Math.Max(candidates.Total, ranked.Count);

        var page = ranked.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        activity?.SetTag("search.total", total);
        return new SearchResultPage(
            page, total, query.Page, query.PageSize, stopwatch.Elapsed.TotalMilliseconds, Facets: candidates.Facets);
    }

    /// <summary>
    /// The products whose pictures are nearest the words, nearest first — or
    /// none. None when the leg is off (the default), when the query sits inside
    /// a category (the picture store knows nothing of branches), when this
    /// process has no picture model or store, and when either fails or is
    /// slow: **the pictures can only add to a search, never cost it**. A failure
    /// is counted as a degradation of its own stage; never having had the
    /// stage is not.
    /// </summary>
    private async Task<IReadOnlyList<string>> LooksAsync(
        ProductSearchQuery query, HybridSearchOptions settings, Activity? activity, CancellationToken ct)
    {
        if (settings.PictureDepth <= 0 || !String.IsNullOrEmpty(query.Category) || !pictureModel.IsConfigured)
            return [];

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(settings.PictureBudget);

            var vector = await pictureModel.EmbedTextAsync(query.Text, budget.Token);
            var near = await pictures.NearestAsync(vector, settings.PictureDepth, ct: budget.Token);
            return [.. near.Select(match => match.ProductId)];
        }
        catch (SearchStageNotConfiguredException)
        {
            return [];
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            SearchDegradation.Record(activity, SearchDegradation.PictureStage, exception, logger);
            return [];
        }
    }

    private async Task<List<SearchHit>> RerankAsync(
        ProductSearchQuery query, List<SearchHit> ranked, HybridSearchOptions settings, Activity? activity, CancellationToken ct)
    {
        var head = ranked.Take(settings.RerankDepth).ToList();
        if (head.Count < 2)
            return ranked;

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(settings.RerankBudget);

            var texts = await vectors.TextsAsync([.. head.Select(hit => hit.ProductId)], query.Culture, budget.Token);

            // The embedded text when there is one; the card's name when a
            // product's vector is missing, which is thin and still better than
            // leaving it out of the comparison.
            var inputs = head
                .Select(hit => texts.TryGetValue(hit.ProductId, out var text) && text.Length > 0 ? text : hit.Name)
                .ToList();

            var scores = await reranker.ScoreAsync(query.Text, inputs, budget.Token);

            if (scores.Count != head.Count)
                throw new SearchUnavailableException("reranking", $"{scores.Count} scores came back for {head.Count} texts");

            activity?.SetTag("search.reranked", head.Count);

            var reordered = head
                .Select((hit, position) => (Hit: hit, Score: scores[position], Position: position))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Position)
                .Select(candidate => candidate.Hit with { Score = candidate.Score });

            return [.. reordered, .. ranked.Skip(head.Count)];
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            SearchDegradation.Record(activity, SearchDegradation.RerankStage, exception, logger);
            return ranked;
        }
    }
}

/// <summary>
/// Where a degradation surfaces: on the span, on a counter, and in the log.
/// </summary>
internal static class SearchDegradation
{
    public const string VectorStage = "vector";
    public const string RerankStage = "rerank";
    public const string PictureStage = "pictures";
    public const string Tag = "search.degraded";
    public const string CounterName = "tendero.search.degraded";

    /// <summary>
    /// A stage this process was never given, tagged on the span and never counted:
    /// a machine with no GPU and no reranker is not a reranker that is failing.
    /// </summary>
    public const string NotConfiguredTag = "search.stage.unavailable";

    private static readonly Meter Meter = new(TelemetrySources.Search);

    private static readonly Counter<long> Degraded = Meter.CreateCounter<long>(
        CounterName, unit: "{search}", description: "Searches answered without one of their AI stages, by stage.");

    public static void Record(Activity? activity, string stage, Exception exception, ILogger logger)
    {
        activity?.SetTag(Tag, stage);
        activity?.SetTag("search.degraded.reason", exception.Message);
        Degraded.Add(1, new KeyValuePair<string, object?>("stage", stage));

        // An unavailable layer already said so once, when it became unavailable
        // (QdrantVectorLayer); a warning per search would bury that line. Anything
        // else is unexpected and gets its stack.
        if (exception is SearchUnavailableException or OperationCanceledException)
            logger.LogDebug("Search degraded past the {Stage} stage: {Reason}", stage, exception.Message);
        else
            logger.LogWarning(exception, "Search degraded past the {Stage} stage", stage);
    }
}
