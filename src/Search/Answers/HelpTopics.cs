using System.Text.RegularExpressions;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Answers;

internal sealed record HelpTopic(string Code, string Destination, IReadOnlyDictionary<string, string[]> PhrasesByCulture);

/// <summary>
/// Questions about the shop rather than about a product (ADR 0032). 34 % of
/// Baymard's test users searched for one, and it is the query type shops fail most.
///
/// **A topic exists only where its destination exists** — the rule of the article
/// *The feature flag you did not need*, applied to content. Returns has one (<c>GET /api/returns/policy</c>); shipping and contact
/// do not yet, so they are not here, and a search for them is answered like any other.
///
/// Matched as whole words after lower-casing and folding accents with an explicit
/// table: the repository builds with <c>InvariantGlobalization=true</c>, where
/// <c>Normalize(FormD)</c> is a silent no-op.
/// </summary>
internal static partial class HelpTopics
{
    public const string Returns = "returns";

    public static readonly IReadOnlyList<HelpTopic> All =
    [
        new(Returns, Destination(Returns), new Dictionary<string, string[]>
        {
            ["es"] = ["devoluciones", "devolucion", "devolver", "politica de devolucion", "cambios y devoluciones"],
            ["en"] = ["returns", "return policy", "return an order", "send it back", "send back", "refund"]
        })
    ];

    private static string Destination(string code) =>
        HelpTopicDestinations.For(code)
        ?? throw new InvalidOperationException($"No destination configured for help topic '{code}'.");

    public static string? Match(string text, string culture)
    {
        var words = Words(text);

        return All.FirstOrDefault(topic =>
                topic.PhrasesByCulture.TryGetValue(culture, out var phrases)
                && phrases.Any(phrase => ContainsRun(words, Words(phrase))))
            ?.Code;
    }

    private static string[] Words(string text) =>
        WordPattern().Split(Fold(text.ToLowerInvariant()))
            .Where(static word => word.Length > 0)
            .ToArray();

    private static bool ContainsRun(string[] words, string[] run)
    {
        for (var start = 0; start + run.Length <= words.Length; start++)
            if (words.AsSpan(start, run.Length).SequenceEqual(run))
                return true;

        return false;
    }

    private static string Fold(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
            span[i] = source[i] switch
            {
                'á' or 'à' or 'ä' or 'â' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                _ => source[i]
            };
    });

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
