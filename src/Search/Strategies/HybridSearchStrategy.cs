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

    public int RrfK { get; init; } = ReciprocalRankFusion.DefaultK;

    /// <summary>How many fused results the reranker re-sorts.</summary>
    public int RerankDepth { get; init; } = 20;

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
        var candidates = await lexical.SearchAsync(query with { Page = 1, PageSize = depth }, ct);

        var nearest = matches.Select(match => match.ProductId).ToList();
        var words = candidates.Hits.Select(hit => hit.ProductId).ToList();

        var fused = ReciprocalRankFusion.Fuse([words, nearest], settings.RrfK);

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

        if (reranker.IsConfigured)
            ranked = await RerankAsync(query, ranked, settings, activity, ct);
        else
            activity?.SetTag(SearchDegradation.NotConfiguredTag, SearchDegradation.RerankStage);

        // Exact when lexical returned everything it matched, which is the case at
        // a hundred products; otherwise the lexical count, raised if meaning
        // found more than it did.
        var total = candidates.Total <= candidates.Hits.Count
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
    public const string Tag = "search.degraded";
    public const string CounterName = "tendero.search.degraded";

    /// <summary>
    /// A stage this process was never given, tagged on the span and never counted:
    /// a machine with no GPU and no reranker is not a reranker that is failing.
    /// </summary>
    public const string NotConfiguredTag = "search.stage.unavailable";

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
