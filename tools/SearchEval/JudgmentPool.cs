using System.Text.Encodings.Web;
using System.Text.Json;
using ElGuerre.Tendero.Search.Evaluation;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The products some strategy put in its first ten that the golden set has no
/// judgment for — per query, with where each strategy placed them.
///
/// It is what a fair golden set is completed FROM. The set was pooled from lexical
/// query variants only (docs/search-evaluation.md, 2026-09-11), so a product that
/// only meaning finds is unjudged, counts as irrelevant, and hybrid is measured by a
/// ruler lexical built. Judging this pool — every strategy's first ten, not one
/// strategy's — is the fix, and the file is what a person reviews.
/// </summary>
public static class JudgmentPool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,

        // A file a person reads: "Sartén" stays "Sartén" rather than "Sartén".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<PoolCulture> Build(IReadOnlyList<StrategyScores> results)
    {
        var sightings = (
            from result in results
            from culture in result.Cultures
            from query in culture.Queries
            from hit in query.Unjudged
            select new Sighting(culture.Culture, query.Query, result.Strategy, hit)).ToList();

        var cultures = results
            .SelectMany(result => result.Cultures.Select(culture => culture.Culture))
            .Distinct(StringComparer.Ordinal);

        return
        [
            .. cultures.Select(culture => new PoolCulture(
                culture,
                [
                    .. sightings
                        .Where(sighting => sighting.Culture == culture)
                        .GroupBy(sighting => sighting.Query, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => new PoolQuery(group.Key, Candidates(group)))
                ]))
        ];
    }

    public static string ToJson(IReadOnlyList<PoolCulture> pool) => JsonSerializer.Serialize(pool, JsonOptions);

    /// <summary>
    /// One candidate per product, best-placed first: the one some strategy ranked
    /// highest is the one whose missing judgment distorts the score most.
    /// </summary>
    private static IReadOnlyList<PoolCandidate> Candidates(IEnumerable<Sighting> sightings) =>
    [
        .. sightings
            .GroupBy(sighting => sighting.Hit.ExternalId, StringComparer.Ordinal)
            .Select(product => new PoolCandidate(
                product.Key,
                product.First().Hit.Name,
                product
                    .GroupBy(sighting => sighting.Strategy, StringComparer.Ordinal)
                    .ToDictionary(
                        strategy => strategy.Key,
                        strategy => strategy.Min(sighting => sighting.Hit.Position),
                        StringComparer.Ordinal)))
            .OrderBy(candidate => candidate.Positions.Values.Min())
            .ThenBy(candidate => candidate.ExternalId, StringComparer.Ordinal)
    ];

    private sealed record Sighting(string Culture, string Query, string Strategy, UnjudgedHit Hit);
}

public sealed record PoolCulture(string Culture, IReadOnlyList<PoolQuery> Queries);

public sealed record PoolQuery(string Query, IReadOnlyList<PoolCandidate> Candidates);

/// <param name="Positions">Strategy name → where it placed this product, from 1.</param>
public sealed record PoolCandidate(string ExternalId, string Name, IReadOnlyDictionary<string, int> Positions);
