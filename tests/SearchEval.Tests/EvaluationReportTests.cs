using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The report a pull request and a person read. What is held here is the shape
/// that decides a run: a diagnostic strategy is shown and never judged against a
/// threshold it does not have, and the lexical → hybrid comparison survives a
/// third strategy in the list.
/// </summary>
public sealed class EvaluationReportTests
{
    private static readonly EvaluationThresholds Thresholds = new(
        new Dictionary<string, IReadOnlyDictionary<string, CultureThresholds>>(StringComparer.OrdinalIgnoreCase)
        {
            ["lexical"] = new Dictionary<string, CultureThresholds> { ["es"] = new(0.9, 0.9) },
            ["hybrid"] = new Dictionary<string, CultureThresholds> { ["es"] = new(0.9, 0.9) }
        });

    [Fact]
    public void A_diagnostic_strategy_is_reported_without_a_threshold_and_the_comparison_survives_it()
    {
        IReadOnlyList<StrategyScores> results =
        [
            new("lexical", [Culture(Query("mochila", 1.0, judged: 1.0))]),
            new("hybrid", [Culture(Query("mochila", 0.8, judged: 0.7, new UnjudgedHit(2, "B9", "Bolsa")))]),
            new("vector", [Culture(Query("mochila", 0.5, judged: 0.4, new UnjudgedHit(1, "B9", "Bolsa")))], Gated: false)
        ];

        var report = EvaluationReport.Render(results, Thresholds);

        Assert.Contains("| vector | es | 1 | 0.500 | — | 1.000 | — | 0.400 |", report);
        Assert.Contains("diagnostic", report);
        Assert.Contains("## es: lexical → hybrid", report);
        Assert.Contains("## es: judgment coverage", report);

        // One product unjudged by two strategies is one to judge, not two.
        Assert.Contains("| mochila | 1.000 | 0.700 | 0.400 | 0.500 | 1 |", report);
    }

    /// <summary>
    /// The breakdown is the point of classifying queries: an overall mean where
    /// hybrid loses a little on keywords and wins a lot on intent reads as a tie.
    /// </summary>
    [Fact]
    public void Scores_are_broken_down_by_kind_so_a_mean_cannot_hide_where_a_strategy_wins()
    {
        IReadOnlyList<StrategyScores> results =
        [
            new("lexical", [Culture(Kinded("mochila", 1.0, "keyword"), Kinded("cafetera", 0.8, "keyword"), Kinded("regalo para casa", 0.2, "intent"))]),
            new("hybrid", [Culture(Kinded("mochila", 0.9, "keyword"), Kinded("cafetera", 0.7, "keyword"), Kinded("regalo para casa", 0.6, "intent"))])
        ];

        var report = EvaluationReport.Render(results, Thresholds);

        Assert.Contains("## es: NDCG@10 by kind", report);
        Assert.Contains("| keyword | 2 | 0.900 | 0.800 |", report);
        Assert.Contains("| intent | 1 | 0.200 | 0.600 |", report);

        // Nobody split these queries, so there is no split section to render.
        Assert.DoesNotContain("by split", report);
    }

    private static QueryScore Kinded(string text, double ndcg, string kind) =>
        new(text, ndcg, 1, 10, 1, 1, [], kind);

    private static CultureScore Culture(params QueryScore[] queries) => new("es", queries);

    private static QueryScore Query(string text, double ndcg, double judged, params UnjudgedHit[] unjudged) =>
        new(text, ndcg, 1, 10, 1, judged, unjudged);
}
