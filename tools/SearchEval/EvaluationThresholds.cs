using System.Text.Json;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The committed thresholds per culture. Lowering them requires a justification
/// in the PR body (docs/search-evaluation.md): it is the only defence against
/// "move the bar until it passes".
/// </summary>
public sealed record EvaluationThresholds(IReadOnlyDictionary<string, CultureThresholds> Cultures)
{
    public static async Task<EvaluationThresholds> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        var cultures = await JsonSerializer.DeserializeAsync<Dictionary<string, CultureThresholds>>(
            stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);

        return new EvaluationThresholds(cultures
            ?? throw new InvalidDataException($"'{path}' is empty or not a thresholds file."));
    }

    public CultureThresholds For(string culture) =>
        Cultures.TryGetValue(culture, out var thresholds)
            ? thresholds
            : throw new InvalidDataException($"No committed thresholds for culture '{culture}'.");
}

public sealed record CultureThresholds(double NdcgAt10, double RecallAt50);
