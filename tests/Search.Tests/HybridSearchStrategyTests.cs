using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.Strategies;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// Hybrid search, and above all what it does when its AI stages fail (P8-5…P8-8).
///
/// Invariant 8 says every AI feature has a non-AI path and the degradation is
/// TESTED, not asserted in a comment. The test that holds it is the first one
/// here: with a vector side that throws, hybrid's answer is the lexical answer
/// serialised byte for byte — not "some results", not "the same ids".
/// </summary>
public sealed class HybridSearchStrategyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- P8-8: the degradation, byte for byte ----------

    [Fact]
    public async Task HybridDegradesToLexical()
    {
        // Page two of three, so the lexical CANDIDATES — page one, fifty deep —
        // would serialise differently from the lexical answer. A degradation that
        // re-paged the candidates instead of asking again fails here.
        var query = new ProductSearchQuery("cafetera italiana", "es", Page: 2, PageSize: 3);
        var lexical = new Lexical("a", "b", "c", "d", "e", "f", "g");
        var vectors = new Vectors(("x", 0.9)) { Failure = new SearchUnavailableException("vector search", "Qdrant does not answer") };

        var degraded = await Hybrid(lexical, vectors).SearchAsync(query, Ct);
        var plain = await new LexicalSearchStrategy(lexical).SearchAsync(query, Ct);

        Assert.Equal(JsonSerializer.Serialize(plain), JsonSerializer.Serialize(degraded));
    }

    [Fact]
    public async Task A_vector_side_that_never_answers_degrades_the_same_way_within_its_budget()
    {
        var query = new ProductSearchQuery("cafetera", "es", Page: 1, PageSize: 2);
        var lexical = new Lexical("a", "b", "c");
        var options = new HybridSearchOptions { VectorBudget = TimeSpan.FromMilliseconds(50) };

        var stopwatch = Stopwatch.StartNew();
        var degraded = await Hybrid(lexical, new Vectors { Hangs = true }, options: options).SearchAsync(query, Ct);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal(
            JsonSerializer.Serialize(await new LexicalSearchStrategy(lexical).SearchAsync(query, Ct)),
            JsonSerializer.Serialize(degraded));
    }

    /// <summary>
    /// A silent fallback is indistinguishable from bad relevance (P8-7), so each
    /// stage that fails says so where somebody looks: the span and a counter, by stage.
    /// </summary>
    [Theory]
    [InlineData(SearchDegradation.VectorStage)]
    [InlineData(SearchDegradation.RerankStage)]
    public async Task A_degradation_is_tagged_on_the_span_and_counted_by_stage(string stage)
    {
        var text = $"degraded at {stage} {Guid.NewGuid():N}";
        var spans = new List<Activity>();
        var counted = new List<string>();

        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetrySources.Search,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "search.hybrid" && (string?)activity.GetTagItem("search.text") == text)
                    lock (spans) spans.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(activities);

        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetrySources.Search && instrument.Name == SearchDegradation.CounterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "stage")
                    lock (counted) counted.Add((string)tag.Value!);
        });
        meters.Start();

        var failure = new SearchUnavailableException(stage, "down");
        var strategy = stage == SearchDegradation.VectorStage
            ? Hybrid(new Lexical("a", "b"), new Vectors(("a", 0.9)) { Failure = failure })
            : Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9)), reranker: new Reranker(_ => 1) { Failure = failure }, rerank: true);

        await strategy.SearchAsync(new ProductSearchQuery(text, "en"), Ct);

        Assert.Equal(stage, Assert.Single(spans).GetTagItem(SearchDegradation.Tag));
        Assert.Contains(stage, counted);
    }

    /// <summary>The caller giving up is not the vector layer failing: it is not counted and not answered.</summary>
    [Fact]
    public async Task A_caller_that_gives_up_is_not_mistaken_for_a_degradation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Hybrid(new Lexical("a"), new Vectors { Hangs = true })
                .SearchAsync(new ProductSearchQuery("cafetera", "es"), cancelled.Token));
    }

    // ---------- P8-5: the fusion ----------

    [Fact]
    public async Task Meaning_brings_in_what_words_missed_and_agreement_ranks_first()
    {
        var result = await Hybrid(new Lexical("a", "b", "c"), new Vectors(("b", 0.8), ("d", 0.7)), new Cards("d"))
            .SearchAsync(new ProductSearchQuery("regalo para casa", "es"), Ct);

        Assert.Equal(["b", "a", "d", "c"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(4, result.Total);
        Assert.Equal(1d / 62 + 1d / 61, result.Hits[0].Score, precision: 12);
    }

    /// <summary>
    /// A vector for a product the lexical index does not hold as Active — archived,
    /// out of the branch, or left behind by a projection that failed — is not a
    /// card anybody can buy, and it does not appear.
    /// </summary>
    [Fact]
    public async Task A_vector_match_nobody_can_buy_is_dropped()
    {
        var result = await Hybrid(new Lexical("a"), new Vectors(("archived", 0.99), ("a", 0.5)), new Cards())
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["a"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task A_match_below_the_minimum_similarity_is_not_a_match()
    {
        var options = new HybridSearchOptions { MinimumSimilarity = 0.5 };

        var result = await Hybrid(new Lexical("a"), new Vectors(("near", 0.62), ("far", 0.31)), new Cards("near", "far"), options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["a", "near"], result.Hits.Select(hit => hit.ProductId));
    }

    /// <summary>
    /// No product carries every word, so the lexical list is loose matches: asked
    /// to, meaning's order goes first and the words follow. With a full match, or
    /// switched off, the fusion stands — and switched off nobody is asked.
    /// </summary>
    [Theory]
    [InlineData(true, false, new[] { "meant", "near", "loose" }, 1)]
    [InlineData(true, true, new[] { "meant", "loose", "near" }, 1)]
    [InlineData(false, false, new[] { "meant", "loose", "near" }, 0)]
    public async Task Without_a_full_match_meaning_can_go_first(bool meaningFirst, bool anyFullMatch, string[] expected, int asked)
    {
        var fullMatch = new FullMatch(anyFullMatch);

        var result = await Hybrid(new Lexical("loose", "meant"), new Vectors(("meant", 0.7), ("near", 0.6)), new Cards("near"),
                options: new HybridSearchOptions { MeaningFirstWithoutFullMatch = meaningFirst }, fullMatch: fullMatch)
            .SearchAsync(new ProductSearchQuery("poner la mesa", "es"), Ct);

        Assert.Equal(expected, result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(asked, fullMatch.Asked);
    }

    /// <summary>First in words and first in meaning tie, and a tie goes to the words — unless meaning is weighted up.</summary>
    [Fact]
    public async Task A_heavier_vector_weight_lets_meaning_outrank_the_words()
    {
        var equal = await Hybrid(new Lexical("typed"), new Vectors(("meant", 0.8)), new Cards("meant"))
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        var weighted = await Hybrid(new Lexical("typed"), new Vectors(("meant", 0.8)), new Cards("meant"),
                options: new HybridSearchOptions { VectorWeight = 1.5 })
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["typed", "meant"], equal.Hits.Select(hit => hit.ProductId));
        Assert.Equal(["meant", "typed"], weighted.Hits.Select(hit => hit.ProductId));
    }

    /// <summary>
    /// A browse has no words to mean anything, and "newest first" is an order the
    /// shopper chose: neither goes near the vector side, so neither can degrade.
    /// </summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("cafetera", "newest")]
    public async Task A_browse_and_a_date_order_are_answered_by_lexical_alone(string text, string? sort)
    {
        var query = new ProductSearchQuery(text, "es", Category: "KITCHEN", Sort: sort);
        var lexical = new Lexical("a", "b");
        var vectors = new Vectors(("z", 0.9)) { Failure = new InvalidOperationException("must not be asked") };

        var result = await Hybrid(lexical, vectors).SearchAsync(query, Ct);

        Assert.Equal(0, vectors.Searches);
        Assert.Equal(JsonSerializer.Serialize(await new LexicalSearchStrategy(lexical).SearchAsync(query, Ct)), JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task A_page_is_a_window_on_the_fused_list_and_fetches_deep_enough_to_fill_it()
    {
        var lexical = new Lexical("a", "b", "c", "d", "e");
        var options = new HybridSearchOptions { LexicalDepth = 2 };

        var page = await Hybrid(lexical, new Vectors(("e", 0.9)), options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es", Page: 2, PageSize: 2), Ct);

        // Two pages of two need four candidates, more than the configured depth.
        Assert.Equal(4, lexical.Asked.Single().PageSize);
        Assert.Equal(1, lexical.Asked.Single().Page);
        Assert.Equal((2, 2), (page.Page, page.PageSize));

        // Fused a, b, c, d (e has no card and is dropped): page two is the second pair,
        // not the first pair counted twice.
        Assert.Equal(["c", "d"], page.Hits.Select(hit => hit.ProductId));
    }

    [Fact]
    public async Task Without_a_cut_the_total_is_what_the_engine_matched_not_what_it_returned()
    {
        var result = await Hybrid(new Lexical("a", "b") { Total = 40 }, new Vectors(("b", 0.9)))
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(40, result.Total);
    }

    [Fact]
    public async Task A_match_exactly_at_the_minimum_similarity_is_a_match()
    {
        var result = await Hybrid(new Lexical("a"), new Vectors(("edge", 0.5)), new Cards("edge"),
                options: new HybridSearchOptions { MinimumSimilarity = 0.5 })
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["a", "edge"], result.Hits.Select(hit => hit.ProductId));
    }

    // ---------- P8-6: the reranker ----------

    [Fact]
    public async Task The_reranker_reorders_the_head_from_the_embedded_texts_and_leaves_the_tail()
    {
        var vectors = new Vectors(("b", 0.9))
        {
            Texts = new() { ["a"] = "text of a", ["b"] = "text of b" }
        };
        var reranker = new Reranker(text => text == "text of a" ? 0.99 : text == "text of b" ? 0.10 : 0.5);
        var options = new HybridSearchOptions { RerankDepth = 2 };

        var result = await Hybrid(new Lexical("a", "c"), vectors, new Cards("b"), reranker, rerank: true, options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        // Fused: a (1/61), b (1/61, from the later ranking), c (1/62). The head
        // [a, b] is reranked by the embedded texts, c stays where fusion put it.
        Assert.Equal(["a", "b", "c"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(0.99, result.Hits[0].Score);

        var reversed = new Reranker(text => text == "text of b" ? 0.99 : 0.10);
        var swapped = await Hybrid(new Lexical("a", "c"), vectors, new Cards("b"), reversed, rerank: true, options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["b", "a", "c"], swapped.Hits.Select(hit => hit.ProductId));
    }

    [Fact]
    public async Task Results_the_reranker_scores_alike_keep_the_order_fusion_gave_them()
    {
        var result = await Hybrid(new Lexical("a", "c"), new Vectors(("b", 0.9)), new Cards("b"), new Reranker(_ => 0.5), rerank: true)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["a", "b", "c"], result.Hits.Select(hit => hit.ProductId));
    }

    [Fact]
    public async Task A_product_with_no_embedded_text_is_reranked_by_its_name()
    {
        var vectors = new Vectors { Texts = new() { ["a"] = "", ["b"] = "text of b" } };
        var reranker = new Reranker(text => text == "Product a" ? 0.99 : 0.10);

        var result = await Hybrid(new Lexical("b", "a"), vectors, reranker: reranker, rerank: true)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(["a", "b"], result.Hits.Select(hit => hit.ProductId));
    }

    [Fact]
    public async Task A_reranker_past_its_budget_leaves_the_fused_order_standing()
    {
        var options = new HybridSearchOptions { RerankBudget = TimeSpan.FromMilliseconds(50) };
        var reranker = new Reranker(_ => 1) { Hangs = true };

        var result = await Hybrid(new Lexical("a", "b", "c"), new Vectors(("c", 0.9)), reranker: reranker, rerank: true, options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        // Fused: c is in both rankings (1/63 + 1/61), then a (1/61), then b (1/62).
        Assert.Equal(["c", "a", "b"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(1, reranker.Calls);
    }

    [Fact]
    public async Task With_the_rerank_flag_off_the_reranker_is_never_asked()
    {
        var reranker = new Reranker(_ => 1) { Failure = new InvalidOperationException("must not be asked") };

        var result = await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9)), reranker: reranker, rerank: false)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);

        Assert.Equal(0, reranker.Calls);
        Assert.Equal(["b", "a"], result.Hits.Select(hit => hit.ProductId));
    }

    // ---------- H4: the reranker can say "this is not an answer" ----------

    [Fact]
    public async Task A_result_the_reranker_turns_away_is_not_shown_and_neither_is_the_unread_rest()
    {
        var reranker = new Reranker(text => text == "text of a" ? 0.8 : 0.02);

        // The engine claims forty matches it did not return: once the reranker has
        // turned results away, the total is what is left, not the engine's claim.
        var result = await Reranked(reranker, minimum: 0.1, lexicalTotal: 40);

        // Head [a, b]: b scores 0.02 and is turned away, so c — below the head and
        // never read by the reranker — goes too.
        Assert.Equal(["a"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task When_the_whole_head_clears_the_minimum_the_rest_follows_it()
    {
        var reranker = new Reranker(text => text == "text of a" ? 0.8 : 0.4);

        var result = await Reranked(reranker, minimum: 0.1);

        Assert.Equal(["a", "b", "c"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task A_score_exactly_at_the_minimum_is_still_an_answer()
    {
        var result = await Reranked(new Reranker(_ => 0.1), minimum: 0.1);

        Assert.Equal(["a", "b", "c"], result.Hits.Select(hit => hit.ProductId));
    }

    [Fact]
    public async Task When_the_reranker_turns_everything_away_the_answer_is_empty()
    {
        var result = await Reranked(new Reranker(_ => 0.001), minimum: 0.1);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.Total);
    }

    // ---------- H5: a query whose best result reads as unrelated has no answer ----------

    [Fact]
    public async Task When_even_the_best_result_scores_under_the_bar_the_query_has_no_answer()
    {
        var result = await Reranked(new Reranker(text => text == "text of a" ? 0.04 : 0.01), minimum: 0, noAnswerBelow: 0.05, lexicalTotal: 40);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task One_result_at_the_bar_is_enough_and_nothing_is_cut()
    {
        // b scores far below the bar and stays: the decision is about the query, not each result.
        var result = await Reranked(new Reranker(text => text == "text of a" ? 0.05 : 0.001), minimum: 0, noAnswerBelow: 0.05);

        Assert.Equal(["a", "b", "c"], result.Hits.Select(hit => hit.ProductId));
    }

    /// <summary>Lexical [a, c], meaning [b]; fused a, b, c and a head of two for the reranker.</summary>
    private static Task<SearchResultPage> Reranked(Reranker reranker, double minimum, long? lexicalTotal = null, double noAnswerBelow = 0)
    {
        var vectors = new Vectors(("b", 0.9)) { Texts = new() { ["a"] = "text of a", ["b"] = "text of b" } };
        var options = new HybridSearchOptions { RerankDepth = 2, MinimumRerankScore = minimum, NoAnswerBelowRerankScore = noAnswerBelow };

        return Hybrid(new Lexical("a", "c") { Total = lexicalTotal }, vectors, new Cards("b"), reranker, rerank: true, options: options)
            .SearchAsync(new ProductSearchQuery("cafetera", "es"), Ct);
    }
}
