using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.Knowledge.Chat;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.SearchEval.Claims;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// An extractor against the golden claim set (P10-0, the seed of P10-9), through
/// the same <see cref="IClaimExtractor"/> the product will call — so the numbers
/// are the numbers of what ships. The candidates, the decisions and the
/// hypotheses declared before any of this ran are in
/// <c>docs/analysis/claim-extraction-models.md</c>.
/// </summary>
internal sealed class ClaimsSuite : IEvaluationSuite
{
    /// <summary>The attributes a description can state a value for; colour, size and gender come from variants and the import.</summary>
    private static readonly HashSet<string> Untyped = ["COLOR", "SIZE", "GENDER", "SIZE_RANGE"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Name => "claims";

    /// <summary>Twenty products at up to a minute each on a CPU (P10-0 measured ~30 s p50, ~50 s p95), with room for a slow machine.</summary>
    public TimeSpan Budget => TimeSpan.FromMinutes(60);

    public string Description => "claim extraction against knowledge/claims.json: precision, recall, typing, verbatim quotes, traps";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var golden = JsonSerializer.Deserialize<ClaimGoldenSet>(
            await File.ReadAllTextAsync(Path.Combine(options.ClaimsDirectory!, "claims.json"), cancellationToken))!;
        var products = await ProductTexts.ReadAsync(options.SeedPath, cancellationToken);
        var attributes = await ReadAttributesAsync(options.AttributesPath!, cancellationToken);

        var (extractor, name) = options.Extractor switch
        {
            ClaimExtractors.Rules => ((IClaimExtractor)new RulesClaimExtractor(), "rules"),
            ClaimExtractors.Chat when options.Model is { Length: > 0 } model =>
                (new ChatClaimExtractor(options.Onnx is { Length: > 0 } folder
                    ? InProcessOnnxChat.Create(folder)
                    : OpenAICompatibleChat.Create(new Uri(options.Chat!), model)), model),
            ClaimExtractors.Chat => throw new ArgumentException("--extractor chat needs --model: the label the report gives what the endpoint serves."),
            var other => throw new ArgumentException($"Unknown extractor '{other}'. Available: rules, chat."),
        };

        var judged = new List<ProductVerdicts>();
        foreach (var product in golden.Products)
        {
            var text = products[product.Index];
            var clock = Stopwatch.StartNew();
            IReadOnlyList<ProposedClaim> predicted;
            string? error = null;
            try
            {
                predicted = await extractor.ExtractAsync(text, attributes, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A model that breaks is a result, not a crash — a server that drops
                // the connection surfaces as a cancellation nobody asked for.
                (predicted, error) = ([], exception.Message);
            }

            var (verdicts, missed) = ClaimScoring.Judge(product, text, predicted);
            judged.Add(new ProductVerdicts(product.Code, error, clock.Elapsed.TotalSeconds, verdicts, missed));
            Console.Error.WriteLine($"  {product.Code}: {verdicts.Count} claims, {clock.Elapsed.TotalSeconds:F1}s");
        }

        var score = ClaimScoring.Summarise(golden, judged);
        Console.WriteLine(Line(name, score));

        if (options.ReportPath is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { extractor = name, score, products = judged }, Indented), cancellationToken);

        // The floor for this kind of extractor (P10-9); an extractor with none is measured, not judged.
        var gates = await KnowledgeGates.LoadAsync(options.ClaimsDirectory!, cancellationToken);
        return gates.Claims.TryGetValue(options.Extractor ?? "", out var gate)
            ? GateVerdict.Report(Name, options.Extractor!, gate.Failures(score), options.FailUnderThresholds)
            : score.Errors > 0 ? 2 : 0;
    }

    internal static string Line(string name, ClaimScore s) => string.Create(CultureInfo.InvariantCulture,
        $"{name,-22} P {s.Precision:F2}  R {s.Recall:F2}  Rnew {s.RecallNew:F2}  typed {s.TypedAccuracy:F2}  spurious {s.Spurious,2}  " +
        $"verbatim {s.VerbatimSpanish:F3}  lang {s.InTheirLanguage?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a"}  traps {s.Traps}  " +
        $"errors {s.Errors}  p50 {s.LatencyP50Seconds:F1}s  p95 {s.LatencyP95Seconds:F1}s");

    private static async Task<IReadOnlyList<AttributeSpec>> ReadAttributesAsync(string path, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        return
        [
            .. document.RootElement.EnumerateArray()
                .Where(attribute => !Untyped.Contains(attribute.GetProperty("code").GetString()!))
                .Select(attribute => new AttributeSpec(
                    attribute.GetProperty("code").GetString()!,
                    attribute.GetProperty("kind").GetString()!,
                    attribute.TryGetProperty("unit", out var unit) ? unit.GetString() : null,
                    attribute.GetProperty("label").GetProperty("en").GetString()!,
                    attribute.TryGetProperty("options", out var choices)
                        ? [.. choices.EnumerateArray().Select(option => option.GetProperty("code").GetString()!)]
                        : [])),
        ];
    }
}

/// <summary>The seed catalogue's descriptions, by position — the golden sets name products by index and code.</summary>
internal static class ProductTexts
{
    public static async Task<IReadOnlyList<ProductText>> ReadAsync(string seedPath, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken));
        return
        [
            .. document.RootElement.EnumerateArray().Select(product => new ProductText(
                product.GetProperty("description").GetProperty("es").GetString()!,
                product.GetProperty("description").GetProperty("en").GetString()!)),
        ];
    }
}
