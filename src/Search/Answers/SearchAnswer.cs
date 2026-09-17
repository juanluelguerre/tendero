using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// What kind of answer a search page is (ADR 0032). No model decides this: both
/// values are a plain fact about the query and the index.
///
/// <para>
/// There were three (ADR 0032, amended 2026-09-17). <c>OutOfScope</c> belonged to
/// a scope classifier that was measured by its declared protocol and refuted, and
/// it could never be sent: with no calibration the classifier never registered,
/// so the wire carried a value nothing could produce. The classifier and its
/// harness are evaluation tooling now (<c>tools/SearchEval/QueryScope</c>).
/// </para>
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
    Related
}

/// <param name="Match">What kind of answer this is.</param>
/// <param name="HelpTopic">
/// The non-product intent the query matched, if any — for example "returns".
/// A help card above the products, never a redirect: the word still finds
/// products too.
/// </param>
public sealed record SearchAnswer(SearchMatch Match, string? HelpTopic = null);

/// <summary>
/// The port: what kind of answer a page is. See
/// <see cref="ElGuerre.Tendero.Search.Answers.SearchAnswerer"/> for the
/// implementation, decided with no model.
/// </summary>
public interface ISearchAnswerer
{
    /// <summary>Null for a browse or a date order: there is no query to judge.</summary>
    Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default);
}
