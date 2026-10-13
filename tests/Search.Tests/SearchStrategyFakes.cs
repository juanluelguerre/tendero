using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The deterministic doubles the strategy tests share. Each one answers from a
/// list, so a test states what each engine "found" and reads the fusion off the
/// result — no mock expectations to keep in step with the implementation.
/// </summary>
internal static class Strategies
{
    public static SearchHit Hit(string productId, bool inStock = true) =>
        new(productId, $"CODE-{productId}", $"Product {productId}", $"product-{productId}", "Kuisina", "KITCHEN",
            $"{productId}-variant", $"{productId}-SKU", new Money(29.90m, "EUR"), new Money(29.90m, "EUR"), new Money(34.90m, "EUR"), null, inStock, Score: 1.5);

    public static HybridSearchStrategy Hybrid(
        ILexicalProductSearch lexical,
        IVectorProductSearch vectors,
        ISearchHitReader? cards = null,
        IReranker? reranker = null,
        HybridSearchOptions? options = null,
        IImageEmbeddingGenerator? pictureModel = null,
        IPictureSearch? pictures = null) =>
        new(lexical, vectors, cards ?? new Cards(), reranker ?? new NoReranker(),
            pictureModel ?? new NoImageEmbeddings(), pictures ?? new NoPictureVectors(),
            Options.Create(options ?? new HybridSearchOptions()), NullLogger<HybridSearchStrategy>.Instance);

    /// <summary>A lexical engine that "matched" these products, in this order.</summary>
    public sealed class Lexical(params string[] matched) : ILexicalProductSearch
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        /// <summary>What the engine claims it matched, when that is more than it returned.</summary>
        public long? Total { get; init; }

        /// <summary>
        /// Whether it answers the union the way Elasticsearch does: the products
        /// in <see cref="ProductSearchQuery.AlsoMatching"/> that no word matched
        /// come after the words' matches, scoring zero.
        /// </summary>
        public bool AnswersTheUnion { get; init; }

        /// <summary>The facets it counted, handed back as they are.</summary>
        public IReadOnlyList<SearchFacet>? Facets { get; init; }

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);

            var byMeaning = AnswersTheUnion
                ? (query.AlsoMatching ?? []).Where(id => !matched.Contains(id)).ToArray()
                : [];

            var hits = matched.Select(id => Hit(id))
                .Concat(byMeaning.Select(id => Hit(id) with { Score = 0 }))
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            return Task.FromResult(new SearchResultPage(
                hits, Total ?? matched.Length + byMeaning.Length, query.Page, query.PageSize, TookMs: 7, Facets: Facets));
        }
    }

    /// <summary>A vector side that found these products, nearest first — or fails, or never answers.</summary>
    public sealed class Vectors(params (string Id, double Similarity)[] nearest) : IVectorProductSearch
    {
        public Exception? Failure { get; init; }
        public bool Hangs { get; init; }
        public Dictionary<string, string> Texts { get; init; } = [];
        public int Searches { get; private set; }

        public async Task<IReadOnlyList<VectorMatch>> SearchAsync(
            string text, string culture, string? category, int limit, CancellationToken ct = default)
        {
            Searches++;

            if (Hangs)
                await Task.Delay(Timeout.Infinite, ct);

            if (Failure is not null)
                throw Failure;

            return [.. nearest.Take(limit).Select(match => new VectorMatch(match.Id, match.Similarity))];
        }

        public Task<IReadOnlyDictionary<string, string>> TextsAsync(
            IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                productIds.Where(Texts.ContainsKey).ToDictionary(id => id, id => Texts[id]));
    }

    /// <summary>
    /// The lexical index's cards, for the products it knows are Active — and,
    /// when the search is filtered, only for those that pass the filters.
    /// </summary>
    public sealed class Cards(params string[] active) : ISearchHitReader
    {
        /// <summary>The products a filtered search keeps; null keeps every Active one.</summary>
        public string[]? PassingFilters { get; init; }

        public List<SearchFilters> Filtered { get; } = [];

        public Task<IReadOnlyList<SearchHit>> FindAsync(
            IReadOnlyCollection<string> productIds, string culture, string? category, SearchFilters filters,
            CancellationToken ct = default)
        {
            Filtered.Add(filters);

            var kept = productIds.Where(active.Contains);
            if (!filters.IsEmpty && PassingFilters is { } passing)
                kept = kept.Where(passing.Contains);

            return Task.FromResult<IReadOnlyList<SearchHit>>([.. kept.Select(id => Hit(id))]);
        }
    }

    /// <summary>A reranker that scores each text by a rule, or fails, or takes forever.</summary>
    public sealed class Reranker(Func<string, double> score) : IReranker
    {
        public Exception? Failure { get; init; }
        public bool Hangs { get; init; }
        public int Calls { get; private set; }

        public async Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Calls++;

            if (Hangs)
                await Task.Delay(Timeout.Infinite, ct);

            if (Failure is not null)
                throw Failure;

            return [.. texts.Select(score)];
        }
    }
}
