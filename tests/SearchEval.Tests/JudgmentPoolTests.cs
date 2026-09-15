using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The pool a fairer golden set is completed from: every strategy's unjudged
/// first ten, merged per query, so a product two strategies surface is judged once.
/// </summary>
public sealed class JudgmentPoolTests
{
    [Fact]
    public void A_product_two_strategies_surface_unjudged_is_one_candidate_carrying_both_positions()
    {
        IReadOnlyList<StrategyScores> results =
        [
            new("lexical", [Culture("es", Query("regalo para casa"))]),
            new("hybrid", [Culture("es", Query("regalo para casa", new UnjudgedHit(3, "B1", "Taza"), new UnjudgedHit(7, "B2", "Manta")))]),
            new("vector", [Culture("es", Query("regalo para casa", new UnjudgedHit(1, "B1", "Taza")))], Gated: false)
        ];

        var query = Assert.Single(Assert.Single(JudgmentPool.Build(results)).Queries);

        // Best-placed first: B1 was first for the vector side.
        Assert.Equal(["B1", "B2"], query.Candidates.Select(candidate => candidate.ExternalId));
        Assert.Equal((3, 1), (query.Candidates[0].Positions["hybrid"], query.Candidates[0].Positions["vector"]));
        Assert.False(query.Candidates[0].Positions.ContainsKey("lexical"));
    }

    [Fact]
    public void A_query_whose_first_ten_are_all_judged_is_not_in_the_pool()
    {
        IReadOnlyList<StrategyScores> results =
        [
            new("lexical", [Culture("en", Query("coffee maker"))]),
            new("hybrid", [Culture("en", Query("coffee maker"))])
        ];

        Assert.Empty(Assert.Single(JudgmentPool.Build(results)).Queries);
    }

    [Fact]
    public void The_file_keeps_accents_readable()
    {
        IReadOnlyList<StrategyScores> results =
            [new("vector", [Culture("es", Query("sartén", new UnjudgedHit(1, "B3", "Sartén de hierro fundido")))], Gated: false)];

        Assert.Contains("Sartén de hierro fundido", JudgmentPool.ToJson(JudgmentPool.Build(results)));
    }

    private static CultureScore Culture(string culture, params QueryScore[] queries) => new(culture, queries);

    private static QueryScore Query(string text, params UnjudgedHit[] unjudged) =>
        new(text, 1, 1, 10, 1, 1 - unjudged.Length / 10d, unjudged);
}
