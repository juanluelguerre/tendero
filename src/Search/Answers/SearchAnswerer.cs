using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Answers;

/// <summary>
/// What kind of answer a page is (ADR 0032): layer B's verdict when it is sure the
/// shop does not sell this, and layer A's — decided with no model — otherwise.
///
/// It runs after whichever strategy answered, not inside one, so lexical search —
/// the permanent fallback — is exactly as honest as hybrid, and hybrid's
/// byte-for-byte degradation to lexical (P8-8) is untouched by it.
/// </summary>
internal sealed class SearchAnswerer(ILexicalFullMatch fullMatch, IQueryScope scope) : ISearchAnswerer
{
    public async Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || query.Sort is not null)
            return null;

        var help = HelpTopics.Match(query.Text, query.Culture);

        // Layer B first: when the shop clearly does not sell this, the full-match
        // count has nothing to add. Anything else — in scope, ambiguous, or no
        // verdict at all — leaves the decision to layer A (ADR 0032).
        var verdict = await scope.ClassifyAsync(query.Text, query.Culture, ct);
        if (verdict.Outcome == ScopeOutcome.OutOfScope)
            return new SearchAnswer(SearchMatch.OutOfScope, help, verdict.Label);

        return await fullMatch.AnyAsync(query, ct)
            ? new SearchAnswer(SearchMatch.Exact, help)
            : new SearchAnswer(SearchMatch.Related, help);
    }
}
