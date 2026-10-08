using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Domain;
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
///
/// **Two vocabularies.** `whole`, the default: every attribute offered for every
/// product, which is how the floors in `gates.json` were measured. `category`:
/// each product offered what its category's branch declares (ADR 0056), which is
/// what the shop offers its extractor since phase 13a. Each has its own floors
/// (`chat` and `chat/category`): the second is the higher one, because a model
/// offered only what a product can have types more of it right.
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

        var byCategory = options.Vocabulary?.ToLowerInvariant() switch
        {
            null or "whole" => false,
            "category" => true,
            var other => throw new ArgumentException($"Unknown vocabulary '{other}'. Available: whole, category."),
        };
        var categories = byCategory
            ? await CategorySeedFile.LoadAsync(options.CategoriesPath!, TimeProvider.System, cancellationToken)
            : CategoryTree.Empty;
        var types = byCategory ? await ProductTexts.TypesAsync(options.SeedPath, cancellationToken) : [];

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
                predicted = await extractor.ExtractAsync(
                    text, byCategory ? Offered(attributes, categories, types[product.Index]) : attributes, cancellationToken);
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
        Console.WriteLine(Line(byCategory ? $"{name} · by category" : name, score));

        if (options.ReportPath is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { extractor = name, score, products = judged }, Indented), cancellationToken);

        // The floor for this kind of extractor (P10-9); an extractor with none is measured, not judged.
        var gates = await KnowledgeGates.LoadAsync(options.ClaimsDirectory!, cancellationToken);
        var key = GateKey(options.Extractor, byCategory);

        return gates.Claims.TryGetValue(key, out var gate)
            ? GateVerdict.Report(Name, key, gate.Failures(score), options.FailUnderThresholds)
            : score.Errors > 0 ? 2 : 0;
    }

    /// <summary>Which floor a run is held to: the extractor's kind, and the vocabulary it was offered.</summary>
    internal static string GateKey(string? extractor, bool byCategory) =>
        byCategory ? $"{extractor}/category" : extractor ?? "";

    /// <summary>
    /// What a product of this category is offered: of the attributes a
    /// description can state, those its branch declares — and all of them when
    /// the branch has not said (ADR 0056).
    /// </summary>
    internal static IReadOnlyList<AttributeSpec> Offered(IReadOnlyList<AttributeSpec> attributes, CategoryTree categories, string? category) =>
        categories.AttributesOf(category) is { } declared
            ? [.. attributes.Where(attribute => declared.Contains(attribute.Code, StringComparer.OrdinalIgnoreCase))]
            : attributes;

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
    /// <summary>Each product's type, which is its category's code, in the same order.</summary>
    public static async Task<IReadOnlyList<string?>> TypesAsync(string seedPath, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken));
        return [.. document.RootElement.EnumerateArray().Select(product => product.GetProperty("product_type").GetString())];
    }

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
