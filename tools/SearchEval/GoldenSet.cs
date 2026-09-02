using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SearchEval;

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

        return await JsonSerializer.DeserializeAsync<GoldenSet>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"'{path}' is empty or not a golden set.");
    }
}

public sealed record GoldenQuery(string Query, IReadOnlyList<Judgment> Judgments)
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
