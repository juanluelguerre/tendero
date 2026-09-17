using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Answers;

/// <summary>
/// What kind of answer a page is (ADR 0032): whether some product carries every
/// word of the query, decided with no model at all, plus the help topic a
/// non-product question matched.
///
/// It runs after whichever strategy answered, not inside one, so lexical search —
/// the permanent fallback — is exactly as honest as hybrid, and hybrid's
/// byte-for-byte degradation to lexical (P8-8) is untouched by it.
///
/// <para>
/// <b>There is no second layer here</b> (ADR 0032, amended 2026-09-17). A scope
/// classifier that would have added "we don't sell that at all" was measured
/// once by its declared protocol and refuted; it lives with the harness that
/// measured it, in <c>tools/SearchEval/QueryScope</c>, and no shipped code reads
/// it. So the answer has two values and neither needs a model server.
/// </para>
/// </summary>
internal sealed class SearchAnswerer(ILexicalFullMatch fullMatch) : ISearchAnswerer
{
    public async Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || query.Sort is not null)
            return null;

        var help = HelpTopics.Match(query.Text, query.Culture);

        return await fullMatch.AnyAsync(query, ct)
            ? new SearchAnswer(SearchMatch.Exact, help)
            : new SearchAnswer(SearchMatch.Related, help);
    }
}
