using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The arithmetic a <see cref="CultureScore"/> reports, on queries built by hand.
/// Every figure here was worked out on paper. Added at the 2026-09-17 phase close,
/// where Stryker showed that the report's shares could be multiplied instead of
/// divided, and a threshold met by either metric instead of both, with nothing red.
/// </summary>
public sealed class CultureScoreTests
{
    [Fact]
    public void An_empty_culture_reports_zeros_rather_than_throwing()
    {
        var score = new CultureScore("es", []);

        Assert.Equal((0d, 0d, 0d), (score.MeanNdcgAt10, score.MeanRecallAt50, score.MeanMilliseconds));
    }

    [Fact]
    public void Means_are_means_over_the_scored_queries()
    {
        var score = new CultureScore("es",
        [
            Scored(ndcg: 1.0, milliseconds: 2),
            Scored(ndcg: 0.5, milliseconds: 4),
            NoAnswer(QueryKinds.NotSold, match: null) with { Milliseconds = 6 }
        ]);

        Assert.Equal(0.75, score.MeanNdcgAt10, 10);
        Assert.Equal(4, score.MeanMilliseconds, 10);
    }

    [Theory]
    [InlineData(0.8, 0.9, true)]    // exactly on both bars is a pass
    [InlineData(0.79, 0.9, false)]  // NDCG under, recall on
    [InlineData(0.8, 0.89, false)]  // NDCG on, recall under
    public void A_threshold_is_met_only_when_both_metrics_reach_it(double ndcg, double recall, bool met)
    {
        var score = new CultureScore("es", [Scored(ndcg, recall: recall)]);

        Assert.Equal(met, new CultureThresholds(0.8, 0.9).AreMetBy(score));
    }

    [Fact]
    public void The_notice_rate_is_the_share_of_scored_queries_told_nothing_matched_exactly()
    {
        var score = new CultureScore("es",
        [
            Scored(1.0) with { Match = SearchMatch.Related },
            Scored(1.0) with { Match = SearchMatch.Exact },
            NoAnswer(QueryKinds.NotSold, SearchMatch.Related)
        ]);

        Assert.Equal(0.5, score.NoticeRate, 10);
    }

    [Fact]
    public void With_no_scored_queries_the_notice_rate_is_zero()
    {
        var score = new CultureScore("es", [NoAnswer(QueryKinds.NotSold, SearchMatch.Related)]);

        Assert.Equal(0d, score.NoticeRate);
    }

    /// <summary>
    /// Three no-answer queries, one admitted: a not-sold query with a help topic is
    /// not admitted by it (only a non-product question is), and one the answerer
    /// said nothing about is not admitted either — without making the share null.
    /// </summary>
    [Fact]
    public void Admitted_is_the_share_of_no_answer_queries_whose_page_admits_it()
    {
        var score = new CultureScore("es",
        [
            NoAnswer(QueryKinds.NotSold, SearchMatch.Related),
            NoAnswer(QueryKinds.NotSold, SearchMatch.Exact) with { HelpTopic = "returns" },
            NoAnswer(QueryKinds.NotSold, match: null)
        ]);

        Assert.Equal(1d / 3, score.Admitted!.Value, 10);
    }

    private static QueryScore Scored(double ndcg, double recall = 1.0, double milliseconds = 1) =>
        new("query", ndcg, recall, 10, milliseconds, 1.0, [], QueryKinds.Feature, "tune");

    private static QueryScore NoAnswer(string kind, SearchMatch? match) =>
        new("nothing", null, null, 0, 1, null, [], kind, "tune", Match: match);
}
