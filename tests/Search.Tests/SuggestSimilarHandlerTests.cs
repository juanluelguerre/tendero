using System.Diagnostics;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features;
using ElGuerre.Tendero.Search.Features.SuggestSimilar;
using ElGuerre.Tendero.Search.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.SimilarProductsTests;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// <c>GET /api/search/similar</c> and what it does when the vector layer is not
/// there (invariant 8): the row fills from the rule alone — with no layer in the
/// process, with one that fails, and with one that never answers — and says so
/// in <see cref="SimilarProductsResult.RankedBy"/>. And the cards the home's
/// other rows resolve, by id and by code.
///
/// In the metrics collection, which runs alone: a degraded row adds to
/// <c>tendero.search.degraded</c>, and the hybrid tests that assert nothing was
/// counted listen to that same counter.
/// </summary>
[Collection(SearchMetricsCollection.Name)]
public sealed class SuggestSimilarHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ProductProfile[] Shop =
    [
        Profile("MKPT", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM"),
        Profile("DSKA", "DESK_LAMP", true, "COLOR:BLACK"),
        Profile("STX", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM"),
        Profile("DRP", "COFFEE_MAKER"),
        Profile("GNE", "COFFEE_MAKER", false, "MATERIAL:ALUMINIUM"),
        Profile("DSKB", "DESK_LAMP", true, "COLOR:BLACK"),
        Profile("CMP", "DESK_LAMP"),
    ];

    /// <summary>
    /// The rule's own answer for a moka pot (MKPT) then a desk lamp (DSKA), which every
    /// degraded case must equal. Codes as the catalogue writes them: Crockford's
    /// alphabet, no I, L, O or U, since the handler reads a code as the product page does.
    /// </summary>
    private static readonly string[] RuleRow = ["STX", "DSKB", "DRP", "CMP"];

    private static Task<SimilarProductsResult> Ask(IVectorSimilarity similarity, TimeSpan? budget, params string[] codes) =>
        new SuggestSimilarHandler(
                new Profiles(Shop), similarity,
                Options.Create(new HybridSearchOptions { VectorBudget = budget ?? TimeSpan.FromSeconds(5) }),
                NullLogger<SuggestSimilarHandler>.Instance)
            .HandleAsync(new SuggestSimilarQuery(codes, "es", 8), Ct);

    [Fact]
    public async Task With_the_vector_layer_up_the_rules_candidates_are_re_sorted_by_meaning()
    {
        // DRP nearer to MKPT than STX is, CMP nearer to DSKA than DSKB is.
        var similarity = new Similarity(new() { ["id-DRP"] = 0.9, ["id-STX"] = 0.2, ["id-CMP"] = 0.8, ["id-DSKB"] = 0.1 });

        var answer = await Ask(similarity, null, "mkpt", "DSKA");

        Assert.Equal(SimilarityRanking.Embeddings, answer.RankedBy);
        Assert.Equal(["DRP", "CMP", "STX", "DSKB"], answer.Items.Select(item => item.Product.Code));
        Assert.Equal(["MKPT", "DSKA", "MKPT", "DSKA"], answer.Items.Select(item => item.BecauseOf));
    }

    /// <summary>The test invariant 8 asks for: the vector layer taken away, the row still fills — in the rule's order.</summary>
    [Fact]
    public async Task Without_a_vector_layer_in_this_process_the_rule_alone_fills_the_row()
    {
        var answer = await Ask(new NoVectorLayer(), null, "MKPT", "DSKA");

        Assert.Equal(SimilarityRanking.Rule, answer.RankedBy);
        Assert.Equal(RuleRow, answer.Items.Select(item => item.Product.Code));
    }

    [Fact]
    public async Task A_vector_layer_that_fails_leaves_the_rules_row()
    {
        var failing = new Similarity([]) { Failure = new SearchUnavailableException("product similarity", "Qdrant does not answer") };

        var answer = await Ask(failing, null, "MKPT", "DSKA");

        Assert.Equal(SimilarityRanking.Rule, answer.RankedBy);
        Assert.Equal(RuleRow, answer.Items.Select(item => item.Product.Code));
    }

    [Fact]
    public async Task A_vector_layer_that_never_answers_leaves_the_rules_row_within_its_budget()
    {
        var stopwatch = Stopwatch.StartNew();

        var answer = await Ask(new Similarity([]) { Hangs = true }, TimeSpan.FromMilliseconds(50), "MKPT", "DSKA");

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal(SimilarityRanking.Rule, answer.RankedBy);
        Assert.Equal(RuleRow, answer.Items.Select(item => item.Product.Code));
    }

    /// <summary>One candidate per product viewed has no order to change: the vector layer is not asked, and the row says the rule ranked it.</summary>
    [Fact]
    public async Task With_one_candidate_each_the_vector_layer_is_not_asked()
    {
        var similarity = new Similarity(new() { ["id-CMP"] = 0.9 });

        // Both lamps viewed: each one's only neighbour left is CMP.
        var answer = await Ask(similarity, null, "DSKA", "DSKB");

        Assert.Equal(SimilarityRanking.Rule, answer.RankedBy);
        Assert.Equal(0, similarity.Calls);
        Assert.Equal(["CMP"], answer.Items.Select(item => item.Product.Code));
    }

    /// <summary>One neighbourhood with an order to change is enough for the re-rank, even beside ones that have none.</summary>
    [Fact]
    public async Task One_neighbourhood_to_reorder_is_enough_for_the_re_rank()
    {
        var similarity = new Similarity(new() { ["id-DRP"] = 0.9, ["id-STX"] = 0.2 });

        var answer = await Ask(similarity, null, "MKPT", "DSKA", "DSKB");

        Assert.Equal(SimilarityRanking.Embeddings, answer.RankedBy);
        Assert.Equal("DRP", answer.Items[0].Product.Code);
        Assert.Equal(1, similarity.Calls);
    }

    [Fact]
    public async Task A_code_the_index_does_not_hold_asks_nothing_and_answers_an_empty_row()
    {
        var similarity = new Similarity([]);

        var answer = await Ask(similarity, null, "WTHDRAWN9");

        Assert.Empty(answer.Items);
        Assert.Equal(0, similarity.Calls);
    }

    [Fact]
    public async Task The_viewed_products_and_the_sold_out_one_are_never_in_the_row()
    {
        var answer = await Ask(new NoVectorLayer(), null, "MKPT", "STX");

        Assert.Equal(["DRP"], answer.Items.Select(item => item.Product.Code));
    }

    [Fact]
    public async Task Cards_come_back_in_the_order_asked_ids_then_codes_and_a_missing_one_is_left_out()
    {
        var steelId = Guid.NewGuid();
        var profiles = new Profiles([.. Shop, new ProductProfile(Strategies.Hit(steelId.ToString()) with { Code = "STXBY1D1" }, "COFFEE_MAKER", [])]);

        var cards = await new GetProductCardsHandler(profiles).HandleAsync(
            new GetProductCardsQuery([steelId.ToString().ToUpperInvariant(), Guid.NewGuid().ToString()], ["dska", "MKPT", "N0PE"], "es"), Ct);

        Assert.Equal(["STXBY1D1", "DSKA", "MKPT"], cards.Items.Select(card => card.Code));
    }

    /// <summary>The index, as a list: by id, by code, by leaf category.</summary>
    private sealed class Profiles(IReadOnlyList<ProductProfile> all) : IProductProfileReader
    {
        public Task<IReadOnlyList<ProductProfile>> ByIdsAsync(IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductProfile>>([.. all.Where(profile => productIds.Contains(profile.ProductId))]);

        public Task<IReadOnlyList<ProductProfile>> ByCodesAsync(IReadOnlyCollection<string> codes, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductProfile>>([.. all.Where(profile => codes.Contains(profile.Code))]);

        public Task<IReadOnlyList<ProductProfile>> InCategoriesAsync(IReadOnlyCollection<string> categories, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductProfile>>([.. all.Where(profile => profile.Category is { } category && categories.Contains(category))]);
    }

    /// <summary>A vector layer that knows these similarities — or fails, or never answers.</summary>
    private sealed class Similarity(Dictionary<string, double> scores) : IVectorSimilarity
    {
        public Exception? Failure { get; init; }
        public bool Hangs { get; init; }
        public int Calls { get; private set; }

        public async Task<IReadOnlyDictionary<string, double>> SimilarityAsync(
            string productId, IReadOnlyCollection<string> candidateIds, string culture, CancellationToken ct = default)
        {
            Calls++;

            if (Hangs)
                await Task.Delay(Timeout.Infinite, ct);

            if (Failure is not null)
                throw Failure;

            return candidateIds.Where(scores.ContainsKey).ToDictionary(id => id, id => scores[id]);
        }
    }
}
