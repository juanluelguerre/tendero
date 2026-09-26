using ElGuerre.Tendero.SearchEval.Suites;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The two numbers a support scorer is judged by: whether it ranks supported
/// pairs above the rest (AUC), and whether its number means what it says (ECE) —
/// the reranker of P10-0 did the first and not the second.
/// </summary>
public sealed class SupportSuiteTests
{
    [Fact]
    public void Auc_is_one_when_every_supported_pair_scores_above_every_other_and_ties_count_half()
    {
        Assert.Equal(1.0, SupportSuite.Auc([0.9, 0.8], [0.1, 0.2]));
        Assert.Equal(0.0, SupportSuite.Auc([0.1], [0.9]));
        Assert.Equal(0.5, SupportSuite.Auc([0.5], [0.5]));
    }

    /// <summary>A scorer that ranks perfectly and is still badly calibrated — the reranker's shape.</summary>
    [Fact]
    public void A_perfect_ranking_can_still_be_badly_calibrated()
    {
        ScoredPair Pair(string label, double support) => new(new SupportPair(0, "", "", label), support, 0);

        IReadOnlyList<ScoredPair> underconfident = [Pair(Labels.Supports, 0.3), Pair(Labels.Supports, 0.3), Pair(Labels.NotStated, 0.0), Pair(Labels.NotStated, 0.0)];
        IReadOnlyList<ScoredPair> calibrated = [Pair(Labels.Supports, 1.0), Pair(Labels.Supports, 1.0), Pair(Labels.NotStated, 0.0), Pair(Labels.NotStated, 0.0)];

        Assert.Equal(1.0, SupportSuite.Auc([0.3, 0.3], [0.0, 0.0]));
        Assert.True(SupportSuite.Ece(underconfident) > 0.3);
        Assert.Equal(0.0, SupportSuite.Ece(calibrated), 6);
    }
}
