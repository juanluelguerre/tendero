using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Search.Contracts;
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

    /// <summary>A cosine similarity below which a vector match is not a match. Nearest-neighbour search always answers.</summary>
    public double MinimumSimilarity { get; init; }

    public int RrfK { get; init; } = ReciprocalRankFusion.DefaultK;

    /// <summary>How much the vector ranking counts in the fusion, against lexical's 1. Equal votes unless measured otherwise.</summary>
    public double VectorWeight { get; init; } = 1d;

    /// <summary>
    /// When no product carries every word, answer in meaning's order with the
    /// lexical matches after it, instead of fusing loose word matches as a ranking.
    /// Off until measured; off, the extra count is never asked.
    /// </summary>
    public bool MeaningFirstWithoutFullMatch { get; init; }

    /// <summary>How many fused results the reranker re-sorts.</summary>
    public int RerankDepth { get; init; } = 20;

    /// <summary>
    /// A reranker score, from 0 to 1, below which a reranked result is not an answer.
    /// Once the reranker rejects any of the head, nothing past the head is shown
    /// either: it ranked below what was just rejected, and the reranker never read it.
    /// 0 keeps everything, which is the default until measured.
    /// </summary>
    public double MinimumRerankScore { get; init; }

    /// <summary>
    /// A reranker score, from 0 to 1, that at least ONE result of the head has to
    /// reach for the query to have an answer at all. Decided per query rather than
    /// per result: below it, the shop has nothing for what was asked and says so with
    /// an empty page; at or above it, nothing is cut. 0 keeps everything, which is
    /// the default until measured.
    /// </summary>
    public double NoAnswerBelowRerankScore { get; init; }

    /// <summary>Embedding the query and asking Qdrant, together. Past it, the answer is lexical.</summary>
    public TimeSpan VectorBudget { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Reading the texts and scoring them, together. Past it, the fused order stands.</summary>
    public TimeSpan RerankBudget { get; init; } = TimeSpan.FromMilliseconds(800);
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
/// A browse — no words — and a date order are answered by lexical alone: there is
/// no meaning to add to "everything in the kitchen", and fusing relevance into
/// "newest first" would break the order the shopper asked for.
/// </summary>
internal sealed class HybridSearchStrategy(
    ILexicalProductSearch lexical,
    ILexicalFullMatch fullMatch,
    IVectorProductSearch vectors,
    ISearchHitReader cards,
    IReranker reranker,
    IFeatureGate features,
    IOptions<HybridSearchOptions> options,
    ILogger<HybridSearchStrategy> logger) : ISearchStrategy
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || query.Sort is not null)
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
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            SearchDegradation.Record(activity, SearchDegradation.VectorStage, exception, logger);
            return await lexical.SearchAsync(query, ct);
        }

        var depth = Math.Max(settings.LexicalDepth, query.Page * query.PageSize);
        var candidates = await lexical.SearchAsync(query with { Page = 1, PageSize = depth }, ct);

        var nearest = matches
            .Where(match => match.Similarity >= settings.MinimumSimilarity)
            .Select(match => match.ProductId)
            .ToList();

        var words = candidates.Hits.Select(hit => hit.ProductId).ToList();

        // Loose word matches are not a ranking worth fusing: when nothing carries
        // every word, "mesa" in a desk lamp would otherwise outvote meaning.
        var meaningFirst = settings.MeaningFirstWithoutFullMatch && !await fullMatch.AnyAsync(query, ct);
        activity?.SetTag("search.hybrid.meaning_first", meaningFirst);

        var fused = meaningFirst
            ? ReciprocalRankFusion.Fuse([[.. nearest, .. words]], settings.RrfK)
            : ReciprocalRankFusion.Fuse([words, nearest], settings.RrfK, [1d, settings.VectorWeight]);

        var byProduct = new Dictionary<string, SearchHit>(StringComparer.Ordinal);
        foreach (var hit in candidates.Hits)
            byProduct.TryAdd(hit.ProductId, hit);

        var foundByMeaningOnly = fused.Select(item => item.Id).Where(id => !byProduct.ContainsKey(id)).ToList();
        if (foundByMeaningOnly.Count > 0)
        {
            foreach (var hit in await cards.FindAsync(foundByMeaningOnly, query.Culture, query.Category, ct))
                byProduct.TryAdd(hit.ProductId, hit);
        }

        // An id with no card is a vector nobody can buy — archived, out of the
        // branch, or left behind by a projection that failed — and it is dropped.
        var ranked = fused
            .Where(item => byProduct.ContainsKey(item.Id))
            .Select(item => byProduct[item.Id] with { Score = item.Score })
            .ToList();

        activity?.SetTag("search.hybrid.lexical", candidates.Hits.Count);
        activity?.SetTag("search.hybrid.vector", nearest.Count);
        activity?.SetTag("search.hybrid.meaning_only", ranked.Count(hit => foundByMeaningOnly.Contains(hit.ProductId)));

        var fusedCount = ranked.Count;

        if (features.IsEnabled(FeatureId.SearchRerank))
            ranked = await RerankAsync(query, ranked, settings, activity, ct);

        // Exact when lexical returned everything it matched, which is the case at
        // a hundred products; otherwise the lexical count, raised if meaning
        // found more than it did. When the reranker turned results away, the count
        // is what is left: a total promising pages that do not exist is a lie.
        var total = ranked.Count < fusedCount || candidates.Total <= candidates.Hits.Count
            ? ranked.Count
            : Math.Max(candidates.Total, ranked.Count);

        var page = ranked.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        activity?.SetTag("search.total", total);
        return new SearchResultPage(page, total, query.Page, query.PageSize, stopwatch.Elapsed.TotalMilliseconds);
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

            // The whole head reads as unrelated to the query: "móvil para mi madre de
            // 70 años" in a shop with no phones. An empty page is the honest answer,
            // and a page of the nearest slippers is not.
            if (scores.Max() < settings.NoAnswerBelowRerankScore)
            {
                activity?.SetTag("search.rerank.no_answer", true);
                return [];
            }

            var answers = head
                .Select((hit, position) => (Hit: hit, Score: scores[position], Position: position))
                .Where(candidate => candidate.Score >= settings.MinimumRerankScore)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Position)
                .ToList();

            activity?.SetTag("search.rerank.rejected", head.Count - answers.Count);

            // What follows the head ranked below it before reranking, so once the
            // reranker has turned part of the head away, the unread rest is not an
            // answer either — which is what lets "móvil para mi madre de 70 años"
            // come back empty in a shop with no phones.
            IEnumerable<SearchHit> unread = answers.Count == head.Count ? ranked.Skip(head.Count) : [];

            return [.. answers.Select(candidate => candidate.Hit with { Score = candidate.Score }), .. unread];
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
    public const string Tag = "search.degraded";
    public const string CounterName = "tendero.search.degraded";

    private static readonly Meter Meter = new(TelemetrySources.Search);

    private static readonly Counter<long> Degraded = Meter.CreateCounter<long>(
        CounterName, description: "Searches answered without one of their AI stages, by stage.");

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
