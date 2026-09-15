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
/// twenty candidates — on a GPU. Every failure is a
/// <see cref="SearchUnavailableException"/>, and a timeout is its most common one.
/// </summary>
public interface IReranker
{
    /// <summary>One relevance score per text, in the order the texts were given.</summary>
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>The reranker of a process started without one: it refuses, and the fused order stands.</summary>
internal sealed class NoReranker : IReranker
{
    public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new SearchUnavailableException("reranking", "this process was started without a reranker");
}
