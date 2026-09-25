using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.Strategies;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What any strategy owes the endpoint that does not change (roadmap B1), written
/// once and inherited — the shape every port in this repository already has.
/// </summary>
public abstract class SearchStrategyContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A strategy over a lexical engine that matched these products, in order.</summary>
    protected abstract ISearchStrategy Over(ILexicalProductSearch lexical);

    [Fact]
    public async Task A_browse_is_the_lexical_answer_whichever_strategy_gives_it()
    {
        var lexical = new Lexical("a", "b", "c");
        var query = new ProductSearchQuery("", "en", Category: "KITCHEN");

        Assert.Equal(
            JsonSerializer.Serialize(await lexical.SearchAsync(query, Ct)),
            JsonSerializer.Serialize(await Over(lexical).SearchAsync(query, Ct)));
    }

    [Fact]
    public async Task A_page_says_which_page_it_is_and_holds_no_more_than_it_was_asked_for()
    {
        var page = await Over(new Lexical("a", "b", "c", "d", "e")).SearchAsync(new ProductSearchQuery("cafetera", "es", Page: 2, PageSize: 2), Ct);

        Assert.Equal((2, 2), (page.Page, page.PageSize));
        Assert.InRange(page.Hits.Count, 0, 2);
    }

    /// <summary>The result is a product, whatever matched (ADR 0015): never the same one twice.</summary>
    [Fact]
    public async Task Every_product_appears_at_most_once()
    {
        var page = await Over(new Lexical("a", "b", "c")).SearchAsync(new ProductSearchQuery("cafetera", "es", PageSize: 50), Ct);

        Assert.Equal(page.Hits.Count, page.Hits.Select(hit => hit.ProductId).Distinct().Count());
        Assert.True(page.Total >= page.Hits.Count);
    }
}

public sealed class LexicalSearchStrategyContractTests : SearchStrategyContractTests
{
    protected override ISearchStrategy Over(ILexicalProductSearch lexical) => new LexicalSearchStrategy(lexical);
}

/// <summary>Hybrid with every stage working, including a reranker, so the contract is held by the full path.</summary>
public sealed class HybridSearchStrategyContractTests : SearchStrategyContractTests
{
    protected override ISearchStrategy Over(ILexicalProductSearch lexical) =>
        Hybrid(lexical, new Vectors(("c", 0.9), ("z", 0.8), ("a", 0.7)), new Cards("z"), new Reranker(text => text.Length));
}

/// <summary>
/// Where the storefront, the MCP tools and UCP all go hybrid, since
/// <c>SearchVector</c> met its removal condition (ADR 0023, amended 2026-09-17).
/// Lexical is not asked here any more: it is what hybrid degrades to, byte for
/// byte, and that is <c>HybridDegradesToLexical</c>'s claim, not this handler's.
/// </summary>
public sealed class SearchProductsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Asserts only that hybrid answers every search since it became the
    /// default (ADR 0023, amended 2026-09-17). The degradation to lexical byte
    /// for byte is <c>HybridDegradesToLexical</c>'s claim, not this one's.
    /// </summary>
    [Fact]
    public async Task Hybrid_answers_every_search()
    {
        var hybrid = new Recording("hybrid");

        await new SearchProductsHandler(hybrid, new FixedAnswer(null), new SystemActorAccessor())
            .HandleAsync(new SearchProductsQuery("cafetera", "en", Page: 3, PageSize: 7, Category: "KITCHEN", Sort: "newest"), Ct);

        Assert.Equal(new ProductSearchQuery("cafetera", "en", 3, 7, "KITCHEN", "newest"), Assert.Single(hybrid.Asked));
    }

    [Fact]
    public async Task The_handler_attaches_the_answer_to_the_page_hybrid_returned()
    {
        var expected = new SearchAnswer(SearchMatch.Related);

        var page = await new SearchProductsHandler(new Recording("hybrid"), new FixedAnswer(expected), new SystemActorAccessor())
            .HandleAsync(new SearchProductsQuery("regalo abuela", "es", 1, 20), Ct);

        Assert.Equal(expected, page.Answer);
        Assert.Equal("hybrid", Assert.Single(page.Hits).ProductId);
    }

    private sealed class FixedAnswer(SearchAnswer? answer) : ISearchAnswerer
    {
        public Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default) => Task.FromResult(answer);
    }

    private sealed class Recording(string name) : ISearchStrategy
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);
            return Task.FromResult(new SearchResultPage([Hit(name)], 1, query.Page, query.PageSize, 1));
        }
    }
}
