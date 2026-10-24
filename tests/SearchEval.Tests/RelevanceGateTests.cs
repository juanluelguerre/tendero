using ElGuerre.Tendero.Search.Evaluation;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// What fails the relevance gate: a gated strategy under its thresholds, and —
/// since 2026-10-24 — scores measured against other judgments than the ones the
/// thresholds were set against. Passing those would compare two numbers that do
/// not measure the same thing; failing on them is the gate refusing to compare.
/// A diagnostic strategy never fails it.
/// </summary>
public sealed class RelevanceGateTests
{
    private static readonly EvaluationThresholds Thresholds = new(
        new Dictionary<string, IReadOnlyDictionary<string, CultureThresholds>>(StringComparer.OrdinalIgnoreCase)
        {
            ["lexical"] = new Dictionary<string, CultureThresholds> { ["es"] = new(0.7, 0.7) },
        },
        GoldenSet: "0123456789ab");

    private static StrategyScores Scored(double ndcg, bool gated = true) =>
        new(gated ? "lexical" : "vector", [new CultureScore("es", [new QueryScore("mochila", ndcg, 1, 10, 1, 1, [])])], gated);

    [Fact]
    public void Scores_over_their_thresholds_against_the_same_judgments_pass() =>
        Assert.Empty(RelevanceGate.Failures([Scored(0.9)], Thresholds, "0123456789ab"));

    [Fact]
    public void A_gated_strategy_under_its_thresholds_fails() =>
        Assert.Equal(["lexical es"], RelevanceGate.Failures([Scored(0.5)], Thresholds, "0123456789ab"));

    [Fact]
    public void A_diagnostic_strategy_never_fails() =>
        Assert.Empty(RelevanceGate.Failures([Scored(0.9), Scored(0.1, gated: false)], Thresholds, "0123456789ab"));

    /// <summary>Good scores against other judgments are not a pass: they were not measured against these thresholds.</summary>
    [Fact]
    public void Scores_against_other_judgments_fail_however_good()
    {
        var failure = Assert.Single(RelevanceGate.Failures([Scored(0.9)], Thresholds, "ba9876543210"));

        Assert.Contains("golden set ba9876543210", failure, StringComparison.Ordinal);
        Assert.Contains("0123456789ab", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Thresholds_that_name_no_golden_set_fail_too()
    {
        var unnamed = Thresholds with { GoldenSet = null };

        Assert.Single(RelevanceGate.Failures([Scored(0.9)], unnamed, "0123456789ab"));
    }
}
