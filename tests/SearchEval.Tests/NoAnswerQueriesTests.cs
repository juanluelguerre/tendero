using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// Queries nothing in the catalogue can answer — "movil para mi madre de 70 años"
/// in a shop with no phones, "devoluciones" in a search over products. They are
/// not scored and not pooled; what is reported is whether a strategy answers anyway.
/// </summary>
public sealed class NoAnswerQueriesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_with_nothing_to_find_is_neither_scored_nor_sent_to_be_judged()
    {
        var golden = new GoldenSet("es", "seed",
            [new GoldenQuery("movil para mi madre de 70 años", [], QueryKinds.NotSold, "holdout", "X06")]);

        var score = await new RelevanceEvaluator(new Answering(Hit("p1", "Lámpara de noche con sensor de movimiento")))
            .RunAsync(golden, new Dictionary<string, string> { ["p1"] = "B12LAM0609" }, Ct);

        var query = Assert.Single(score.Queries);

        Assert.Null(query.NdcgAt10);
        Assert.Null(query.JudgedAt10);
        Assert.Empty(query.Unjudged);
        Assert.Equal(("Lámpara de noche con sensor de movimiento", 1), (query.FirstResult, query.Returned));
    }

    [Fact]
    public async Task An_ordinary_query_is_still_pooled_when_nobody_judged_what_it_found()
    {
        var golden = new GoldenSet("es", "seed",
            [new GoldenQuery("riñonera", [new Judgment("B13BAG0708", 3, "rinonera")], QueryKinds.ProductType, "holdout", "N13")]);

        var score = await new RelevanceEvaluator(new Answering(Hit("p1", "Riñonera Cinturón 3L"), Hit("p2", "Neceser de viaje Aseo")))
            .RunAsync(golden, new Dictionary<string, string> { ["p1"] = "B13BAG0708", ["p2"] = "B13BAG0709" }, Ct);

        var query = Assert.Single(score.Queries);
        var unjudged = Assert.Single(query.Unjudged);
        Assert.Equal((2, "B13BAG0709"), (unjudged.Position, unjudged.ExternalId));

        // An ordinary query reports how much of its first ten the ruler can see.
        Assert.Equal(0.5, query.JudgedAt10);
    }

    [Fact]
    public void The_report_shows_what_each_strategy_answered_and_leaves_them_out_of_the_scores()
    {
        IReadOnlyList<StrategyScores> results =
        [
            new("lexical", [new CultureScore("es", [
                new QueryScore("mochila", 1.0, 1.0, 10, 1, 1.0, [], QueryKinds.ProductType, "tune", "Mochila urbana"),
                new QueryScore("ambientador casa", null, null, 2, 1, null, [], QueryKinds.NotSold, "tune", "Zapatillas de casa Hogar")])]),
            new("hybrid", [new CultureScore("es", [
                new QueryScore("mochila", 0.9, 1.0, 10, 1, 1.0, [], QueryKinds.ProductType, "tune", "Mochila urbana"),
                new QueryScore("ambientador casa", null, null, 20, 1, null, [], QueryKinds.NotSold, "tune", "Cesta de almacenaje")])])
        ];

        var report = EvaluationReport.Render(results, new EvaluationThresholds(
            new Dictionary<string, IReadOnlyDictionary<string, CultureThresholds>>(StringComparer.OrdinalIgnoreCase)
            {
                ["lexical"] = new Dictionary<string, CultureThresholds> { ["es"] = new(0.5, 0.5) },
                ["hybrid"] = new Dictionary<string, CultureThresholds> { ["es"] = new(0.5, 0.5) }
            }));

        Assert.Contains("## es: queries with nothing to find", report);

        // Two results shown by lexical, ten by hybrid (the first page caps at ten);
        // neither strategy was asked, so both admitted columns read "—".
        Assert.Contains("| ambientador casa | not-sold | 2 | 10 | — | — | Zapatillas de casa Hogar | Cesta de almacenaje |", report);

        // Not a row of the comparison, and not a kind with an NDCG.
        Assert.DoesNotContain("| ambientador casa | n/a", report);
        Assert.DoesNotContain("| not-sold | 1 |", report);
    }

    [Fact]
    public async Task A_no_answer_query_is_admitted_when_the_page_says_nothing_matches_exactly()
    {
        var golden = new GoldenSet("es", "seed",
        [
            new GoldenQuery("móvil para mi madre de 70 años", [], QueryKinds.NotSold, "holdout", "X06"),
            new GoldenQuery("cafetera italiana", [new Judgment("B07COOK01", 3, "italian coffee maker")], QueryKinds.Feature, "holdout", "X07")
        ]);

        var answerer = new AnswerByQuery(new()
        {
            ["móvil para mi madre de 70 años"] = new SearchAnswer(SearchMatch.Related),
            ["cafetera italiana"] = new SearchAnswer(SearchMatch.Exact)
        });

        var score = await new RelevanceEvaluator(new Answering(Hit("p1", "Lámpara")), answerer)
            .RunAsync(golden, new Dictionary<string, string> { ["p1"] = "B12LAM0609" }, Ct);

        Assert.Equal(1d, score.Admitted);
        Assert.Equal(0d, score.NoticeRate);
    }

    [Fact]
    public async Task A_non_product_query_is_admitted_by_its_help_topic()
    {
        var golden = new GoldenSet("es", "seed",
            [new GoldenQuery("devoluciones", [], QueryKinds.NonProduct, "holdout", "X08")]);

        var answerer = new AnswerByQuery(new()
        {
            ["devoluciones"] = new SearchAnswer(SearchMatch.Exact, HelpTopic: "returns")
        });

        var score = await new RelevanceEvaluator(new Answering(Hit("p1", "Cafetera")), answerer)
            .RunAsync(golden, new Dictionary<string, string> { ["p1"] = "B07COOK01" }, Ct);

        Assert.Equal(1d, score.Admitted);
    }

    [Fact]
    public async Task Without_an_answerer_nothing_is_claimed()
    {
        var golden = new GoldenSet("es", "seed",
        [
            new GoldenQuery("móvil para mi madre de 70 años", [], QueryKinds.NotSold, "holdout", "X06"),
            new GoldenQuery("cafetera italiana", [new Judgment("B07COOK01", 3, "italian coffee maker")], QueryKinds.Feature, "holdout", "X07")
        ]);

        var score = await new RelevanceEvaluator(new Answering(Hit("p1", "Lámpara")))
            .RunAsync(golden, new Dictionary<string, string> { ["p1"] = "B12LAM0609" }, Ct);

        Assert.Null(score.Admitted);
        Assert.Equal(0d, score.NoticeRate);
    }

    private static SearchHit Hit(string productId, string name) =>
        new(productId, "CODE", name, "slug", null, null, "variant", "SKU", 10, 10, 10, "EUR", null, true, 1);

    private sealed class Answering(params SearchHit[] hits) : ISearchStrategy
    {
        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(new SearchResultPage(hits, hits.Length, query.Page, query.PageSize, 1));
    }

    /// <summary>Layer A's fake: what the page told the shopper about a query, decided by the test.</summary>
    private sealed class AnswerByQuery(Dictionary<string, SearchAnswer?> answers) : ISearchAnswerer
    {
        public Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(answers.GetValueOrDefault(query.Text));
    }
}
