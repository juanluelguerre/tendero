using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// What kind of answer a search page is (ADR 0032, layer A). No model decides
/// this: every value is a plain fact about the query and the index.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchMatch>))]
public enum SearchMatch
{
    /// <summary>
    /// At least one active product — in the requested category branch, if any —
    /// carries EVERY word of the query in the fields that say what a product is.
    /// The results are matches, not just the closest things on the shelf.
    /// </summary>
    Exact,

    /// <summary>
    /// No active product carries every word of the query. The results are the
    /// closest things the shop sells, not matches — and the page has to say so
    /// rather than let them pass as an exact answer.
    /// </summary>
    Related,

    /// <summary>
    /// Layer B decided the query names something the shop does not sell at all.
    /// Set only by the query-scope classifier, never by layer A.
    /// </summary>
    OutOfScope
}

/// <param name="Match">What kind of answer this is.</param>
/// <param name="HelpTopic">
/// The non-product intent the query matched, if any — for example "returns".
/// A help card above the products, never a redirect: the word still finds
/// products too.
/// </param>
/// <param name="OutOfScopeLabel">
/// The outside anchor's label, when <see cref="Contracts.SearchMatch.OutOfScope"/>
/// — what the shop tells the shopper it does not sell.
/// </param>
public sealed record SearchAnswer(SearchMatch Match, string? HelpTopic = null, string? OutOfScopeLabel = null);

/// <summary>
/// Layer A / B's port: what kind of answer a page is. See
/// <see cref="ElGuerre.Tendero.Search.Answers.SearchAnswerer"/> for layer A's
/// implementation, decided with no model.
/// </summary>
public interface ISearchAnswerer
{
    /// <summary>Null for a browse or a date order: there is no query to judge.</summary>
    Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default);
}
