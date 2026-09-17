namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// Where a help topic answers from, keyed by its code — the one fact about
/// <c>HelpTopics</c> a caller outside <c>Search</c> is allowed to know.
///
/// <c>HelpTopics</c> itself stays <c>internal</c>: it is the matcher's own
/// vocabulary (the phrases in each culture, the folding, the whole-word run),
/// and a transport has no business reading that by the back door of an
/// <c>InternalsVisibleTo</c> (ADR 0020 — a transport reaches a slice through
/// its public contract, never through another context's internals). This is
/// that public contract, and it is the single source in the direction that
/// matters: <c>HelpTopics.All</c> reads a topic's destination from here, so
/// the address of the returns policy endpoint is written down exactly once.
/// </summary>
public static class HelpTopicDestinations
{
    private static readonly IReadOnlyDictionary<string, string> ByCode = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["returns"] = "/api/returns/policy"
    };

    /// <summary>Null when the code names no topic, or a topic with no destination.</summary>
    public static string? For(string code) => ByCode.GetValueOrDefault(code);
}
