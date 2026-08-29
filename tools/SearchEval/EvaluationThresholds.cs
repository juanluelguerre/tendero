using System.Text.Json;

namespace Tendero.SearchEval;

/// <summary>
/// Umbrales comprometidos por cultura. Bajarlos requiere justificarlo en el
/// cuerpo del PR (docs/search-evaluation.md): es la única defensa contra
/// "ajustar el listón hasta que pase".
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
