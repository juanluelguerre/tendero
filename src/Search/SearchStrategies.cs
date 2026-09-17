using ElGuerre.Tendero.Search.Strategies;

namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// How a search is answered (P8-5), keyed by <see cref="SearchStrategies"/>.
///
/// The storefront's contract does not move: <c>GET /api/search</c> answers the
/// same <see cref="SearchResultPage"/> whichever strategy answered it, which is
/// the promise ADR 0004 made before hybrid search existed. What changes is who
/// is asked, and that is decided by one flag in one handler.
/// </summary>
public interface ISearchStrategy
{
    Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default);
}

public static class SearchStrategies
{
    /// <summary>BM25 alone: the permanent fallback (invariant 8).</summary>
    public const string Lexical = "lexical";

    /// <summary>BM25 and meaning, fused by rank, optionally reranked — and lexical whenever either AI stage fails.</summary>
    public const string Hybrid = "hybrid";
}

/// <summary>
/// Whether the shop carries something described by every word of a query — a
/// narrower question than whether a lexical search would find something for it
/// (ADR 0032, spec D5).
///
/// Lexical search keeps a fallback that accepts any one of the words, so a query
/// no product fully matches still comes back with a full list — "poner la mesa
/// para una cena" finds "mesa" in a desk lamp. This port is what layer A's
/// <c>SearchAnswerer</c> asks before deciding whether a page is an exact answer
/// or the closest things the shop sells: it reads only the fields that say what a
/// product IS, and forgives no typos, so it stays honest at catalogue sizes where
/// a description would contain almost any query's words by coincidence.
/// </summary>
public interface ILexicalFullMatch
{
    Task<bool> AnyAsync(ProductSearchQuery query, CancellationToken ct = default);
}

/// <summary>
/// The cards for products that meaning found and words did not.
///
/// A vector search answers with ids, and a card needs a name, a price, an image
/// and whether it is in stock — all of which the lexical index already holds per
/// variant, kept current by the outbox. Building the card from there means a
/// hybrid result cannot show a price the lexical one would not, and a product
/// that is no longer Active — whose vector a failed projection left behind — is
/// simply not found.
/// </summary>
public interface ISearchHitReader
{
    /// <summary>
    /// One hit per product that is Active, in the culture's index and inside the
    /// branch when there is one. Ids with no such product are left out, not errors.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> FindAsync(
        IReadOnlyCollection<string> productIds, string culture, string? category, CancellationToken ct = default);
}

/// <summary>
/// A slower, better model that re-sorts the first results after retrieval
/// narrowed the field (P8-6). Too expensive over a catalogue, affordable over
/// twenty candidates — on a GPU. A configured reranker's failures are a
/// <see cref="SearchUnavailableException"/>, and a timeout is its most common
/// one; a process started without one gets <see cref="NoReranker"/> instead,
/// whose <see cref="IsConfigured"/> is false, which is why it is never asked.
/// </summary>
public interface IReranker
{
    /// <summary>False for the stand-in of a process started without a reranker, which is then never asked.</summary>
    bool IsConfigured => true;

    /// <summary>One relevance score per text, in the order the texts were given.</summary>
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>The reranker of a process started without one — which today means a machine with no GPU (ADR 0031).</summary>
internal sealed class NoReranker : IReranker
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new SearchStageNotConfiguredException(SearchDegradation.RerankStage);
}
