using ElGuerre.Tendero.Search.Contracts;
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

    /// <summary>
    /// The overall notice rate cannot tell a vague query — where "nothing matches
    /// exactly" is honest — from a product type the shop answers well, where it is
    /// pedantic. Broken down by kind, it can.
    /// </summary>
    [Fact]
    public void The_notice_rate_is_broken_down_by_kind_so_an_overall_share_cannot_hide_where_it_lands()
    {
        var thresholds = new EvaluationThresholds(
            new Dictionary<string, IReadOnlyDictionary<string, CultureThresholds>>(StringComparer.OrdinalIgnoreCase)
            {
                ["lexical"] = new Dictionary<string, CultureThresholds>
                {
                    ["en"] = new(0.5, 0.5),
                    ["es"] = new(0.5, 0.5)
                }
            });

        IReadOnlyList<StrategyScores> results =
        [
            new("lexical",
            [
                new CultureScore("en",
                [
                    Noticed("coffee maker", QueryKinds.ProductType, SearchMatch.Exact),
                    Noticed("desk lamp", QueryKinds.ProductType, SearchMatch.Exact),
                    Noticed("gift for grandma", QueryKinds.UseCase, SearchMatch.Related),
                    Noticed("staying dry on the bike", QueryKinds.UseCase, SearchMatch.Related),
                    Noticed("merino wool", QueryKinds.Feature, SearchMatch.Exact),
                    Noticed("non-stick pan", QueryKinds.Feature, SearchMatch.Related)
                ]),
                new CultureScore("es",
                [
                    Noticed("cafetera", QueryKinds.ProductType, SearchMatch.Exact),
                    Noticed("regalo abuela", QueryKinds.UseCase, SearchMatch.Related)
                ])
            ])
        ];

        var report = EvaluationReport.Render(results, thresholds);

        Assert.Contains("## en: notice rate by kind", report);
        Assert.Contains("| product-type | 2 | 0.000 | — |", report);
        Assert.Contains("| use-case | 2 | 1.000 | gift for grandma; staying dry on the bike |", report);
        Assert.Contains("| feature | 2 | 0.500 | non-stick pan |", report);

        // Every culture gets its own table: the notice is a fact about words, and
        // the two catalogues are not written with the same ones.
        Assert.Contains("## es: notice rate by kind", report);
        Assert.Contains("| use-case | 1 | 1.000 | regalo abuela |", report);
    }

    private static QueryScore Noticed(string text, string kind, SearchMatch match) =>
        new(text, 0.5, 1, 10, 1, 1, [], kind, Match: match);

    private static QueryScore Kinded(string text, double ndcg, string kind) =>
        new(text, ndcg, 1, 10, 1, 1, [], kind);

    private static CultureScore Culture(params QueryScore[] queries) => new("es", queries);

    private static QueryScore Query(string text, double ndcg, double judged, params UnjudgedHit[] unjudged) =>
        new(text, ndcg, 1, 10, 1, judged, unjudged);
}
