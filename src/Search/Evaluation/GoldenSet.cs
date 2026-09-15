using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Search.Evaluation;

/// <summary>
/// One culture's annotated set. Annotations are identified by
/// <see cref="Judgment.ExternalId"/> and not by the product's internal id: that
/// id is a GUID v7 generated on every import, so a golden set using it would
/// expire each time the catalogue is reimported. The source's id ("B073WXYZ01")
/// is stable, and it can also be read and reviewed in a PR.
/// </summary>
public sealed record GoldenSet(
    string Culture,
    string Source,
    IReadOnlyList<GoldenQuery> Queries)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<GoldenSet> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await LoadAsync(stream, path, cancellationToken);
    }

    public static async Task<GoldenSet> LoadAsync(Stream stream, string origin, CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync<GoldenSet>(stream, JsonOptions, cancellationToken)
        ?? throw new InvalidDataException($"'{origin}' is empty or not a golden set.");
}

/// <param name="Kind">
/// What sort of query this is — keyword, attribute, typo, brand, intent,
/// descriptive, synonym — so a report can say WHERE a strategy wins instead of
/// averaging it away. Null for a query nobody classified.
/// </param>
/// <param name="Split">
/// <c>tune</c> or <c>holdout</c>, decided before anything is tuned: a fusion
/// adjusted against the queries it is then validated on has been fitted to them.
/// </param>
/// <param name="Pair">
/// The id this query shares with its counterpart in the other culture, so the two
/// always land in the same split and one wording cannot leak what the other is
/// validated on.
/// </param>
public sealed record GoldenQuery(
    string Query,
    IReadOnlyList<Judgment> Judgments,
    string? Kind = null,
    string? Split = null,
    string? Pair = null)
{
    public Dictionary<string, int> ToRelevanceMap() =>
        Judgments.ToDictionary(judgment => judgment.ExternalId, judgment => judgment.Relevance);
}

/// <summary>
/// One relevance judgment. <see cref="Why"/> is not a decorative comment: it is
/// what makes the annotation reviewable in a PR. Without it nobody can judge
/// whether the grade is right, and the gate ends up measuring the annotator's
/// prejudices.
/// </summary>
public sealed record Judgment(
    string ExternalId,
    [property: JsonPropertyName("relevance")] int Relevance,
    string Why);

/// <summary>
/// The golden sets and the thresholds the gate commits, compiled into this
/// assembly from <c>tools/SearchEval</c>.
///
/// Embedded rather than copied, so the files a pull request changes are the files
/// the backoffice panel scores with — there is one golden set, and it lives where
/// <c>docs/search-evaluation.md</c> says it does.
/// </summary>
public static class EmbeddedEvaluation
{
    private const string Prefix = "ElGuerre.Tendero.Search.Evaluation.";
    private const string ThresholdsResource = Prefix + "eval.thresholds.json";

    private static Assembly Assembly => typeof(EmbeddedEvaluation).Assembly;

    /// <summary>Every culture's golden set, in the order the gate walks the files.</summary>
    public static async Task<IReadOnlyList<GoldenSet>> GoldenSetsAsync(CancellationToken cancellationToken)
    {
        var sets = new List<GoldenSet>();

        foreach (var name in Assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(Prefix + "golden.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            await using var stream = Assembly.GetManifestResourceStream(name)!;
            sets.Add(await GoldenSet.LoadAsync(stream, name, cancellationToken));
        }

        return sets.Count > 0
            ? sets
            : throw new InvalidDataException("No golden set was compiled into the Search assembly.");
    }

    public static async Task<EvaluationThresholds> ThresholdsAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetManifestResourceStream(ThresholdsResource)
            ?? throw new InvalidDataException("The committed thresholds were not compiled into the Search assembly.");

        return await EvaluationThresholds.LoadAsync(stream, ThresholdsResource, cancellationToken);
    }
}
