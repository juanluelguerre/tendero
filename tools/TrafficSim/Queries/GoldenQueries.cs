using System.Text.Json;

namespace ElGuerre.Tendero.TrafficSim.Queries;

/// <summary>
/// What simulated people and agents type: the search golden set, embedded from
/// <c>tools/SearchEval/golden</c> (ADR 0037). Every query, the ones nothing
/// matches included, so the pulse's zero-result rate and SearchEval's report are
/// talking about the same questions.
/// </summary>
public static class GoldenQueries
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ByCulture = Load();

    public static IReadOnlyList<string> In(string culture) =>
        ByCulture.TryGetValue(culture, out var queries) ? queries : [];

    private static Dictionary<string, IReadOnlyList<string>> Load()
    {
        var assembly = typeof(GoldenQueries).Assembly;
        var loaded = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var culture in new[] { "es", "en" })
        {
            using var stream = assembly.GetManifestResourceStream($"ElGuerre.Tendero.TrafficSim.golden.{culture}.json")
                ?? throw new InvalidOperationException($"The {culture} golden set is not embedded.");
            using var document = JsonDocument.Parse(stream);

            loaded[culture] = [.. document.RootElement.GetProperty("queries").EnumerateArray()
                .Select(query => query.GetProperty("query").GetString()!)
                .Distinct(StringComparer.Ordinal)];
        }

        return loaded;
    }
}
