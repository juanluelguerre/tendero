using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Knowledge.Chat;
using ElGuerre.Tendero.Knowledge.Support;
using ElGuerre.Tendero.SearchEval.Claims;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// A support scorer against knowledge/support.json (P10-0): forty pairs a
/// description supports, contradicts or does not state. A good scorer ranks the
/// supported twenty above the rest (AUC) and means what it says (a low ECE:
/// <em>0.8</em> right eight times in ten).
/// </summary>
internal sealed class SupportSuite : IEvaluationSuite
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Name => "support";

    /// <summary>Forty pairs at a few seconds each for the slowest scorer.</summary>
    public TimeSpan Budget => TimeSpan.FromMinutes(20);

    public string Description => "does the text say the claim: AUC, accuracy and calibration against knowledge/support.json";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var set = JsonSerializer.Deserialize<SupportSet>(
            await File.ReadAllTextAsync(Path.Combine(options.ClaimsDirectory!, "support.json"), cancellationToken))!;
        var products = await ProductTexts.ReadAsync(options.SeedPath, cancellationToken);

        var tei = new HttpClient { BaseAddress = new Uri(new Uri(options.Tei!), "/"), Timeout = TimeSpan.FromMinutes(2) };
        var (scorer, name) = options.Scorer switch
        {
            SupportScorers.Nli => ((ISupportScorer)new TeiNliSupportScorer(tei), "nli"),
            SupportScorers.Reranker => (new TeiRerankerSupportScorer(tei), "reranker"),
            SupportScorers.YesNo when options.Model is { Length: > 0 } model =>
                (new ChatYesNoSupportScorer(OpenAICompatibleChat.Create(new Uri(options.Chat!), model)), $"yesno-{model}"),
            SupportScorers.YesNo => throw new ArgumentException("--scorer yesno needs --model: the label the report gives what the endpoint serves."),
            var other => throw new ArgumentException($"Unknown scorer '{other}'. Available: nli, reranker, yesno."),
        };

        var scored = new List<ScoredPair>();
        foreach (var pair in set.Pairs)
        {
            var clock = Stopwatch.StartNew();
            var support = await scorer.ScoreAsync(products[pair.Index].Spanish, pair.Claim, cancellationToken);
            scored.Add(new ScoredPair(pair, support, clock.Elapsed.TotalSeconds));
        }

        var score = Summarise(scored);
        Console.WriteLine(Line(name, score));

        if (options.ReportPath is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { scorer = name, score, pairs = scored }, Indented), cancellationToken);

        // Only the scorer that ships has a floor; the reranker and the yes/no models are measured for comparison.
        var gates = await KnowledgeGates.LoadAsync(options.ClaimsDirectory!, cancellationToken);
        return gates.Support.TryGetValue(name, out var gate)
            ? GateVerdict.Report(Name, name, gate.Failures(score), options.FailUnderThresholds)
            : 0;
    }

    internal static SupportScore Summarise(IReadOnlyList<ScoredPair> scored)
    {
        double[] Of(string label) => [.. scored.Where(pair => pair.Pair.Label == label).Select(pair => pair.Support)];
        var supported = Of(Labels.Supports);
        double[] rest = [.. scored.Where(pair => pair.Pair.Label != Labels.Supports).Select(pair => pair.Support)];
        var latencies = scored.Select(pair => pair.Seconds).Order().ToArray();

        return new SupportScore(
            Auc(supported, rest),
            Auc(supported, Of(Labels.Contradicts)),
            Auc(supported, Of(Labels.NotStated)),
            scored.Count(pair => pair.Support >= 0.5 == (pair.Pair.Label == Labels.Supports)) / (double)scored.Count,
            Ece(scored),
            supported.Average(), Of(Labels.Contradicts).Average(), Of(Labels.NotStated).Average(),
            latencies[latencies.Length / 2]);
    }

    /// <summary>The chance a supported pair scores above an unsupported one; ties count half.</summary>
    internal static double Auc(double[] positives, double[] negatives) =>
        positives.Sum(p => negatives.Sum(n => p > n ? 1 : p == n ? 0.5 : 0)) / (positives.Length * (double)negatives.Length);

    /// <summary>Expected calibration error over five bins: how far "0.8" is from being right eight times in ten.</summary>
    internal static double Ece(IReadOnlyList<ScoredPair> scored, int bins = 5) =>
        Enumerable.Range(0, bins).Sum(bin =>
        {
            var inside = scored.Where(pair => (int)Math.Min(pair.Support * bins, bins - 1) == bin).ToArray();
            return inside.Length == 0
                ? 0
                : inside.Length / (double)scored.Count * Math.Abs(
                    inside.Average(pair => pair.Support) - inside.Average(pair => pair.Pair.Label == Labels.Supports ? 1.0 : 0.0));
        });

    internal static string Line(string name, SupportScore s) => string.Create(CultureInfo.InvariantCulture,
        $"{name,-22} AUC {s.Auc:F2} (vs contradicts {s.AucVersusContradicts:F2}, vs not stated {s.AucVersusNotStated:F2})  " +
        $"acc@0.5 {s.AccuracyAtHalf:F2}  ECE {s.Ece:F2}  mean support {s.MeanSupported:F2} / {s.MeanContradicted:F2} / {s.MeanNotStated:F2}  p50 {s.LatencyP50Seconds:F2}s");
}

internal static class Labels
{
    public const string Supports = "supports";
    public const string Contradicts = "contradicts";
    public const string NotStated = "not_stated";
}

internal sealed record SupportSet([property: JsonPropertyName("pairs")] IReadOnlyList<SupportPair> Pairs);

internal sealed record SupportPair(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("claim")] string Claim,
    [property: JsonPropertyName("label")] string Label);

internal sealed record ScoredPair(SupportPair Pair, double Support, double Seconds);

internal sealed record SupportScore(
    double Auc, double AucVersusContradicts, double AucVersusNotStated, double AccuracyAtHalf, double Ece,
    double MeanSupported, double MeanContradicted, double MeanNotStated, double LatencyP50Seconds);
