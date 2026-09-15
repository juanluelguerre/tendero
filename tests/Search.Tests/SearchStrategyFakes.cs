using ElGuerre.Tendero.Search.Contracts;
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
            $"{productId}-variant", $"{productId}-SKU", 29.90m, 29.90m, 34.90m, "EUR", null, inStock, Score: 1.5);

    public static HybridSearchStrategy Hybrid(
        ILexicalProductSearch lexical,
        IVectorProductSearch vectors,
        ISearchHitReader? cards = null,
        IReranker? reranker = null,
        bool rerank = false,
        HybridSearchOptions? options = null) =>
        new(lexical, vectors, cards ?? new Cards(), reranker ?? new NoReranker(), new Gate(rerank),
            Options.Create(options ?? new HybridSearchOptions()), NullLogger<HybridSearchStrategy>.Instance);

    /// <summary>A lexical engine that "matched" these products, in this order.</summary>
    public sealed class Lexical(params string[] matched) : ILexicalProductSearch
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);

            var hits = matched
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(id => Hit(id))
                .ToList();

            return Task.FromResult(new SearchResultPage(hits, matched.Length, query.Page, query.PageSize, TookMs: 7));
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

    /// <summary>The lexical index's cards, for the products it knows are Active.</summary>
    public sealed class Cards(params string[] active) : ISearchHitReader
    {
        public Task<IReadOnlyList<SearchHit>> FindAsync(
            IReadOnlyCollection<string> productIds, string culture, string? category, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([.. productIds.Where(active.Contains).Select(id => Hit(id))]);
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

    public sealed class Gate(bool rerank) : IFeatureGate
    {
        public bool IsEnabled(FeatureId feature) => feature == FeatureId.SearchRerank && rerank;
    }
}
