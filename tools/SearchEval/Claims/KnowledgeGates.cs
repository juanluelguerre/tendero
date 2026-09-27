using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SearchEval.Claims;

/// <summary>
/// The claims layer's quality gates (P10-9): a floor per extractor and per
/// support scorer, each written beside the measurement it was set from
/// (<c>knowledge/gates.json</c>). The floors sit a little under what P10-0
/// measured — a regression fails, the run-to-run wobble of a model does not.
///
/// Keyed by the extractor's KIND, not the model's name: the chat floor carries
/// the model, revision and prompt it was measured with, and a different one is
/// re-measured before the floor means anything — the file says so rather than
/// a key quietly not matching.
/// </summary>
internal sealed record KnowledgeGates(
    [property: JsonPropertyName("claims")] IReadOnlyDictionary<string, ClaimsGate> Claims,
    [property: JsonPropertyName("support")] IReadOnlyDictionary<string, SupportGate> Support)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<KnowledgeGates> LoadAsync(string directory, CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<KnowledgeGates>(
            await File.ReadAllTextAsync(Path.Combine(directory, "gates.json"), cancellationToken), Json)!;
}

internal sealed record ClaimsGate(
    string MeasuredWith, double Precision, double Recall, double TypedAccuracy, double Verbatim,
    double? Language, int MaxTraps, int MaxErrors)
{
    /// <summary>Every floor the score is under, in words; empty when it passes.</summary>
    public IReadOnlyList<string> Failures(ClaimScore score) =>
    [
        .. Below("precision", score.Precision, Precision),
        .. Below("recall", score.Recall, Recall),
        .. Below("typed accuracy", score.TypedAccuracy, TypedAccuracy),
        .. Below("verbatim quotes", score.VerbatimSpanish, Verbatim),
        .. Language is { } language && score.InTheirLanguage is { } measured ? Below("statements in their language", measured, language) : [],
        .. Above("traps fallen into", score.Traps, MaxTraps),
        .. Above("unreadable answers", score.Errors, MaxErrors),
    ];

    internal static IEnumerable<string> Below(string what, double measured, double floor) =>
        measured < floor ? [string.Create(CultureInfo.InvariantCulture, $"{what} {measured:F3} < {floor:F3}")] : [];

    internal static IEnumerable<string> Above(string what, double measured, double ceiling) =>
        measured > ceiling ? [string.Create(CultureInfo.InvariantCulture, $"{what} {measured} > {ceiling}")] : [];
}

internal sealed record SupportGate(string MeasuredWith, double Auc, double AccuracyAtHalf, double MaxEce)
{
    /// <summary>Ranking and calibration both: a scorer that ranks well and says 0.9 for a coin toss fails (the reranker, P10-0).</summary>
    public IReadOnlyList<string> Failures(Suites.SupportScore score) =>
    [
        .. ClaimsGate.Below("AUC", score.Auc, Auc),
        .. ClaimsGate.Below("accuracy at 0.5", score.AccuracyAtHalf, AccuracyAtHalf),
        .. ClaimsGate.Above("calibration error", Math.Round(score.Ece, 3), MaxEce),
    ];
}

/// <summary>What a gate says about one run, printed the same way by both suites.</summary>
internal static class GateVerdict
{
    /// <returns>The suite's exit code: 1 only when a floor is missed and <c>--ci</c> asked for a verdict.</returns>
    public static int Report(string suite, string key, IReadOnlyList<string> failures, bool failUnderThresholds)
    {
        if (failures.Count == 0)
        {
            Console.WriteLine($"{suite} gate ({key}): pass");
            return 0;
        }

        foreach (var failure in failures)
            Console.WriteLine($"{suite} gate ({key}): FAIL — {failure}");

        return failUnderThresholds ? 1 : 0;
    }
}
