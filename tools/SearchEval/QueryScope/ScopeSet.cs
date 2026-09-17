using System.Text.Json;

namespace ElGuerre.Tendero.SearchEval.Scope;

/// <summary>
/// One culture's scope queries: whether the shop sells anything that answers a
/// query, labelled by a person before the classifier was measured on anything.
///
/// It is a separate file from the golden set on purpose. A golden query is scored
/// on the products it returns; a scope query is scored on one yes-or-no, and most
/// of the "no" half has no product to judge at all.
/// </summary>
public sealed record ScopeSet(string Culture, IReadOnlyList<ScopeQuery> Queries)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<ScopeSet> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ScopeSet>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"'{path}' is empty or not a scope set.");
    }
}

/// <param name="Scope"><c>in</c> or <c>out</c>.</param>
/// <param name="Split">
/// <c>scope-dev</c> or <c>scope-test</c>, decided by the pair id's last digit rather
/// than by anybody's choice: odd is dev, even is test.
/// </param>
/// <param name="Pair">The id this query shares with its counterpart in the other culture.</param>
/// <param name="Kind">
/// What shape of query it is — <c>gift</c>, <c>symptom</c>, <c>use-case</c>,
/// <c>negation</c>, <c>typo</c> for the ones in scope; <c>edge</c> and
/// <c>not-sold</c> for the ones out of it.
/// </param>
/// <param name="Why">The reason for the label, decided against the seed catalogue.</param>
public sealed record ScopeQuery(string Query, string Scope, string Split, string Pair, string Kind, string Why);
