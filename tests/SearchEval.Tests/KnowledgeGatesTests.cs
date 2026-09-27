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

        var failures = gates.Claims["chat"].Failures(Score(0.85, 0.70, typed: 0.80, verbatim: 0.95, language: 0.84, traps: 2, errors: 1));

        // What a red CI run says, word for word: the floor it missed and by how much.
        Assert.Equal(
        [
            "precision 0.850 < 0.880",
            "recall 0.700 < 0.750",
            "typed accuracy 0.800 < 0.830",
            "verbatim quotes 0.950 < 0.970",
            "statements in their language 0.840 < 0.950",
            "traps fallen into 2 > 1",
            "unreadable answers 1 > 0",
        ], failures);
    }

    /// <summary>The reranker's P10-0 figures: it ranks (AUC 0.99) and is not a probability (ECE 0.16).</summary>
    [Fact]
    public async Task A_scorer_that_ranks_but_is_not_calibrated_fails()
    {
        var gates = await KnowledgeGates.LoadAsync(KnowledgeDirectory(), Ct);

        var failures = gates.Support["nli"].Failures(new SupportScore(0.99, 0.99, 0.99, 0.85, 0.16, 0.9, 0.3, 0.3, 1.2));

        Assert.Equal(["accuracy at 0.5 0.850 < 0.950", "calibration error 0.16 > 0.06"], failures);
        Assert.Equal(["AUC 0.900 < 0.970"], gates.Support["nli"].Failures(new SupportScore(0.90, 0.9, 0.9, 1.0, 0.03, 0.9, 0.1, 0.1, 0.1)));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Only_ci_turns_a_missed_floor_into_a_failed_run_and_every_miss_is_printed(bool ci, int exitCode)
    {
        var printed = new StringWriter();
        var original = Console.Out;
        Console.SetOut(printed);
        try
        {
            Assert.Equal(exitCode, GateVerdict.Report("claims", "rules", ["recall 0.200 < 0.280", "traps fallen into 1 > 0"], ci));
            Assert.Equal(0, GateVerdict.Report("claims", "rules", [], ci));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(
            ["claims gate (rules): FAIL — recall 0.200 < 0.280", "claims gate (rules): FAIL — traps fallen into 1 > 0", "claims gate (rules): pass"],
            printed.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

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
