using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ElGuerre.Tendero.Knowledge.Extraction;

namespace ElGuerre.Tendero.SearchEval.Claims;

/// <summary>knowledge/claims.json: products, the claims each description states, and the traps.</summary>
public sealed record ClaimGoldenSet(
    [property: JsonPropertyName("criteria")] IReadOnlyList<string> Criteria,
    [property: JsonPropertyName("products")] IReadOnlyList<GoldenProduct> Products);

public sealed record GoldenProduct(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("claims")] IReadOnlyList<GoldenClaim> Claims,
    [property: JsonPropertyName("traps")] IReadOnlyList<GoldenTrap> Traps);

public sealed record GoldenClaim(
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("attribute")] string? Attribute,
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("known")] bool Known);

/// <summary>A fact the product's attributes carry and its description does not state: proposing it is inventing.</summary>
public sealed record GoldenTrap(
    [property: JsonPropertyName("attribute")] string Attribute,
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("why")] string Why);

public enum Outcome
{
    /// <summary>Its Spanish quote overlaps a golden claim nobody else matched.</summary>
    Match,
    /// <summary>It overlaps a golden claim another prediction already matched.</summary>
    Duplicate,
    /// <summary>Its quote is in the text and states nothing the golden set counts as a claim.</summary>
    Unannotated,
    /// <summary>Its quote is not in the text as written.</summary>
    NotVerbatim,
    /// <summary>It proposes a trap.</summary>
    Trap,
}

public sealed record Verdict(
    ProposedClaim Claim,
    Outcome Outcome,
    int? Golden,
    bool? TypedRight,
    bool Spurious,
    bool VerbatimSpanish,
    bool? VerbatimEnglish,
    bool? InTheirLanguage);

public sealed record ProductVerdicts(
    string Code, string? Error, double LatencySeconds, IReadOnlyList<Verdict> Predictions, IReadOnlyList<string> Missed);

public sealed record ClaimScore(
    int Predictions,
    double Precision,
    double Recall,
    double RecallNew,
    double TypedAccuracy,
    int Spurious,
    double VerbatimSpanish,
    double? VerbatimEnglish,
    double? InTheirLanguage,
    int Traps,
    IReadOnlyDictionary<Outcome, int> Outcomes,
    int Errors,
    double LatencyP50Seconds,
    double LatencyP95Seconds);

/// <summary>
/// How a candidate's claims are judged against the golden set — the rules the
/// P10-0 spike declared, in one place, so every candidate is judged the same way.
/// </summary>
public static partial class ClaimScoring
{
    /// <summary>
    /// Judges one product's predictions. A prediction matches when its Spanish
    /// quote occurs verbatim and overlaps a golden claim's; one golden claim is
    /// matched once, preferring the prediction typed with its attribute, so one
    /// quote can carry two typed claims (<em>"Sartenes de 20, 24 y 28 cm"</em>: three
    /// pieces and three diameters).
    /// </summary>
    public static (IReadOnlyList<Verdict> Verdicts, IReadOnlyList<string> Missed) Judge(
        GoldenProduct golden, ProductText text, IReadOnlyList<ProposedClaim> predicted)
    {
        var goldenSpans = golden.Claims.Select(claim => Spans(text.Spanish, claim.Quote).First()).ToArray();
        var taken = new HashSet<int>();
        var verdicts = new List<Verdict>();

        foreach (var claim in predicted)
        {
            var where = Spans(text.Spanish, claim.QuoteSpanish);
            bool? verbatimEnglish = claim.QuoteEnglish.Length > 0 ? text.English.Contains(claim.QuoteEnglish, StringComparison.Ordinal) : null;
            bool? language = claim.StatementSpanish.Length > 0
                ? InLanguage(claim.StatementSpanish, "es") && InLanguage(claim.StatementEnglish, "en")
                : null;

            Verdict Judged(Outcome outcome, int? match = null, bool? typedRight = null, bool spurious = false) =>
                new(claim, outcome, match, typedRight, spurious, where.Count > 0, verbatimEnglish, language);

            if (golden.Traps.Any(trap => claim.AttributeCode == trap.Attribute && SameValue(claim.Value, trap.Value)))
            {
                verdicts.Add(Judged(Outcome.Trap));
                continue;
            }

            if (where.Count == 0)
            {
                verdicts.Add(Judged(Outcome.NotVerbatim));
                continue;
            }

            var overlapping = Enumerable.Range(0, goldenSpans.Length)
                .Where(i => where.Any(span => span.Start < goldenSpans[i].End && goldenSpans[i].Start < span.End))
                .OrderBy(i => golden.Claims[i].Attribute != claim.AttributeCode)
                .ToArray();
            var free = overlapping.Where(i => !taken.Contains(i)).ToArray();

            if (free.Length == 0)
            {
                verdicts.Add(Judged(overlapping.Length > 0 ? Outcome.Duplicate : Outcome.Unannotated));
                continue;
            }

            var matched = golden.Claims[free[0]];
            taken.Add(free[0]);
            verdicts.Add(Judged(
                Outcome.Match,
                free[0],
                typedRight: matched.Attribute is null ? null : claim.AttributeCode == matched.Attribute && SameValue(claim.Value, matched.Value),
                // Added after the 2B run, and it says so: an attribute on a claim that has none.
                spurious: matched.Attribute is null && claim.AttributeCode is not null));
        }

        var missed = Enumerable.Range(0, golden.Claims.Count).Where(i => !taken.Contains(i)).Select(i => golden.Claims[i].Quote).ToArray();
        return (verdicts, missed);
    }

    public static ClaimScore Summarise(ClaimGoldenSet golden, IReadOnlyList<ProductVerdicts> products)
    {
        var every = products.SelectMany(product => product.Predictions).ToArray();
        var claims = golden.Products.SelectMany(product => product.Claims).ToArray();
        var matched = every.Where(verdict => verdict.Outcome == Outcome.Match).ToArray();
        var matchedNew = products.Zip(golden.Products)
            .Sum(pair => pair.First.Predictions.Count(verdict => verdict.Outcome == Outcome.Match && !pair.Second.Claims[verdict.Golden!.Value].Known));
        var latencies = products.Select(product => product.LatencySeconds).Order().ToArray();

        double? Share(IEnumerable<bool?> values)
        {
            var known = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
            return known.Length > 0 ? known.Count(value => value) / (double)known.Length : null;
        }

        return new ClaimScore(
            every.Length,
            every.Length > 0 ? matched.Length / (double)every.Length : 0,
            matched.Length / (double)claims.Length,
            matchedNew / (double)claims.Count(claim => !claim.Known),
            matched.Count(verdict => verdict.TypedRight == true) / (double)claims.Count(claim => claim.Attribute is not null),
            every.Count(verdict => verdict.Spurious),
            every.Length > 0 ? every.Count(verdict => verdict.VerbatimSpanish) / (double)every.Length : 0,
            Share(every.Select(verdict => verdict.VerbatimEnglish)),
            Share(every.Select(verdict => verdict.InTheirLanguage)),
            every.Count(verdict => verdict.Outcome == Outcome.Trap),
            Enum.GetValues<Outcome>().ToDictionary(outcome => outcome, outcome => every.Count(verdict => verdict.Outcome == outcome)),
            products.Count(product => product.Error is not null),
            Percentile(latencies, 0.5),
            latencies[(int)(0.95 * (latencies.Length - 1))]);
    }

    public static bool SameValue(string? predicted, JsonElement golden) => golden.ValueKind switch
    {
        JsonValueKind.True => predicted?.ToLowerInvariant() is "true" or "si" or "sí" or "yes",
        JsonValueKind.False => predicted?.ToLowerInvariant() is "false" or "no",
        JsonValueKind.Number => double.TryParse(predicted, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                                && Math.Abs(number - golden.GetDouble()) < 1e-6,
        JsonValueKind.String => Squash(predicted) == Squash(golden.GetString()),
        _ => false,
    };

    private static readonly Dictionary<string, HashSet<string>> Stopwords = new()
    {
        ["es"] = ["el", "la", "los", "las", "es", "de", "con", "y", "se", "que", "un", "una", "para", "en", "tiene", "del", "sin"],
        ["en"] = ["the", "is", "of", "with", "and", "a", "an", "it", "for", "in", "has", "to", "can", "without"],
    };

    /// <summary>
    /// Observed, not hypothesised: a statement "in Spanish" written in English
    /// breaks invariant 6. Crude on purpose — function words, counted.
    /// </summary>
    public static bool InLanguage(string text, string language)
    {
        var words = Words().Matches(text.ToLowerInvariant()).Select(match => match.Value).ToArray();
        var other = language == "es" ? "en" : "es";
        return words.Count(Stopwords[language].Contains) >= words.Count(Stopwords[other].Contains);
    }

    private static List<(int Start, int End)> Spans(string text, string quote) =>
        quote.Length == 0
            ? []
            : [.. Regex.Matches(text, Regex.Escape(quote)).Select(match => (match.Index, match.Index + match.Length))];

    private static string Squash(string? value) => (value ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 || p != 0.5
            ? sorted[(int)(p * (sorted.Length - 1))]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

    [GeneratedRegex("[a-záéíóúñü]+")]
    private static partial Regex Words();
}
