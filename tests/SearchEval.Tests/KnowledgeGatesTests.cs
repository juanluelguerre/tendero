using ElGuerre.Tendero.SearchEval.Claims;
using ElGuerre.Tendero.SearchEval.Suites;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The claims layer's gates (P10-9), read from the committed
/// <c>knowledge/gates.json</c>: what P10-0 measured passes, a regression fails and
/// says which floor it missed, and a support scorer that ranks well but is not
/// calibrated fails — the reason the reranker lost to the NLI model.
/// </summary>
public sealed class KnowledgeGatesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClaimScore Score(double precision, double recall, double typed = 0.94, double verbatim = 1.0, double? language = null, int traps = 0, int errors = 0) =>
        new(20, precision, recall, 0.1, typed, 0, verbatim, null, language, traps, new Dictionary<Outcome, int>(), errors, 0, 0);

    [Fact]
    public async Task What_P10_0_measured_passes_its_own_floor()
    {
        var gates = await KnowledgeGates.LoadAsync(KnowledgeDirectory(), Ct);

        Assert.Empty(gates.Claims["rules"].Failures(Score(1.00, 0.30)));
        Assert.Empty(gates.Claims["chat"].Failures(Score(0.92, 0.80, typed: 0.88, verbatim: 0.984, language: 1.00, traps: 1)));
        Assert.Empty(gates.Support["nli"].Failures(new SupportScore(1.00, 1.00, 1.00, 1.00, 0.03, 0.95, 0.1, 0.1, 0.07)));
    }

    [Fact]
    public async Task A_regression_fails_and_names_every_floor_it_missed()
    {
        var gates = await KnowledgeGates.LoadAsync(KnowledgeDirectory(), Ct);

        var failures = gates.Claims["chat"].Failures(Score(0.85, 0.80, typed: 0.88, verbatim: 0.95, language: 0.84, traps: 2, errors: 1));

        Assert.Equal(5, failures.Count);
        Assert.Contains(failures, failure => failure.StartsWith("precision 0.850 < 0.880", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.StartsWith("statements in their language", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.StartsWith("traps fallen into 2 > 1", StringComparison.Ordinal));
    }

    /// <summary>The reranker's P10-0 figures: it ranks (AUC 0.99) and is not a probability (ECE 0.16).</summary>
    [Fact]
    public async Task A_scorer_that_ranks_but_is_not_calibrated_fails()
    {
        var gates = await KnowledgeGates.LoadAsync(KnowledgeDirectory(), Ct);

        var failures = gates.Support["nli"].Failures(new SupportScore(0.99, 0.99, 0.99, 0.85, 0.16, 0.9, 0.3, 0.3, 1.2));

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.StartsWith("calibration error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Only_ci_turns_a_missed_floor_into_a_failed_run(bool ci, int exitCode) =>
        Assert.Equal(exitCode, GateVerdict.Report("claims", "rules", ["recall 0.200 < 0.280"], ci));

    private static string KnowledgeDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory."),
            "tools", "SearchEval", "knowledge");
    }
}
