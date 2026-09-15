using System.Diagnostics;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.SearchEval.Diagnostics;

/// <summary>
/// Meaning alone: the products nearest the query by embedding, with the cards the
/// lexical index holds for them.
///
/// **A diagnostic, never a shop strategy.** It answers the one question the fused
/// number hides: how good is the vector side on its own? A hybrid that loses to
/// lexical could be a weak retrieval or a fusion that drowns a good one, and those
/// are fixed in different places. It has no thresholds and gates nothing, and it
/// lives in the tool rather than in Search so nothing in the shop can be configured
/// to answer with it.
/// </summary>
internal sealed class VectorOnlyStrategy(IVectorProductSearch vectors, ISearchHitReader cards) : ISearchStrategy
{
    public const string Name = "vector";

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var matches = await vectors.SearchAsync(query.Text, query.Culture, query.Category, query.Page * query.PageSize, ct);

        var order = matches.Select(match => match.ProductId).Distinct(StringComparer.Ordinal).ToList();
        var similarity = matches
            .GroupBy(match => match.ProductId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(match => match.Similarity), StringComparer.Ordinal);

        // The cards come back in no promised order, so the vector order is restored
        // here; a product with no card (not Active, not in this culture) drops out,
        // exactly as it does in the hybrid strategy.
        var found = (await cards.FindAsync(order, query.Culture, query.Category, ct))
            .ToDictionary(hit => hit.ProductId, StringComparer.Ordinal);

        var ranked = order
            .Where(found.ContainsKey)
            .Select(id => found[id] with { Score = similarity[id] })
            .ToList();

        var page = ranked.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        return new SearchResultPage(page, ranked.Count, query.Page, query.PageSize, stopwatch.Elapsed.TotalMilliseconds);
    }
}
