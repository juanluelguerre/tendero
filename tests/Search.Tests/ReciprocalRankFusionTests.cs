using CsCheck;
using ElGuerre.Tendero.Search.Strategies;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The fusion, against values worked out by hand (k = 60, positions from one):
/// a function that decides which product a shopper sees first is not tested
/// against its own output.
/// </summary>
public sealed class ReciprocalRankFusionTests
{
    [Fact]
    public void Each_item_scores_one_over_k_plus_its_position_summed_over_the_rankings()
    {
        var fused = ReciprocalRankFusion.Fuse([["a", "b", "c"], ["b", "d"]]);

        Assert.Equal(["b", "a", "d", "c"], fused.Select(item => item.Id));
        Assert.Equal(1d / 62 + 1d / 61, fused[0].Score, precision: 12);
        Assert.Equal(1d / 61, fused[1].Score, precision: 12);
        Assert.Equal(1d / 62, fused[2].Score, precision: 12);
        Assert.Equal(1d / 63, fused[3].Score, precision: 12);
    }

    /// <summary>
    /// Agreement is the point: second in both beats first in one, which is what
    /// lets meaning promote a product that words ranked low without letting a
    /// confident vector outvote the lexical top result on its own.
    /// </summary>
    [Fact]
    public void Second_in_both_rankings_beats_first_in_only_one()
    {
        var fused = ReciprocalRankFusion.Fuse([["words", "both"], ["meaning", "both"]]);

        Assert.Equal("both", fused[0].Id);
    }

    [Fact]
    public void A_tie_goes_to_the_earlier_ranking_so_the_words_somebody_typed_win_it()
    {
        var fused = ReciprocalRankFusion.Fuse([["typed"], ["meant"]]);

        Assert.Equal(["typed", "meant"], fused.Select(item => item.Id));
    }

    [Fact]
    public void A_tie_on_everything_else_goes_to_the_id_so_the_same_inputs_give_one_answer()
    {
        var fused = ReciprocalRankFusion.Fuse([["b", "a"], ["a", "b"]]);

        Assert.Equal(fused[0].Score, fused[1].Score, precision: 12);
        Assert.Equal(["a", "b"], fused.Select(item => item.Id));
    }

    [Fact]
    public void An_id_listed_twice_in_one_ranking_counts_once_at_its_best_place()
    {
        var fused = ReciprocalRankFusion.Fuse([["a", "a", "b"]]);

        Assert.Equal(1d / 61, fused.Single(item => item.Id == "a").Score, precision: 12);
        Assert.Equal(1d / 63, fused.Single(item => item.Id == "b").Score, precision: 12);
    }

    [Fact]
    public void A_smaller_k_rewards_the_top_of_a_ranking_more()
    {
        var fused = ReciprocalRankFusion.Fuse([["a"]], k: 1);

        Assert.Equal(0.5, fused[0].Score, precision: 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse([["a"]], k: 0));
        Assert.Empty(ReciprocalRankFusion.Fuse([[], []]));
    }

    /// <summary>
    /// Whatever the rankings: every id they mention comes out exactly once,
    /// nothing else does, the scores never rise down the list, and asking twice
    /// gives the same order.
    /// </summary>
    [Fact]
    public void Fusion_is_the_union_once_each_in_falling_score_and_deterministic()
    {
        var ranking = Gen.Int[0, 12].Select(id => $"p{id}").List[0, 15];

        Gen.Select(ranking, ranking).Sample((lexical, vector) =>
        {
            var fused = ReciprocalRankFusion.Fuse([lexical, vector]);
            var ids = fused.Select(item => item.Id).ToList();

            Assert.Equal(lexical.Concat(vector).Distinct().Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.True(fused.Zip(fused.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));
            Assert.Equal(ids, ReciprocalRankFusion.Fuse([lexical, vector]).Select(item => item.Id));
        }, iter: 2_000);
    }
}
