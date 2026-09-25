using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// `tendero.search.requests` (the tracking plan), heard from the handler every
/// transport dispatches: typed or browsed, narrowed to a category or not,
/// found or empty — and who asked, through what.
/// </summary>
[Collection(SearchMetricsCollection.Name)]
public sealed class SearchRequestsMetricTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_typed_search_that_finds_something_is_counted_as_found()
    {
        using var heard = new InstrumentRecorder("tendero.search.requests");

        await Handler(hits: 1, new Actor(ActorKinds.Agent, Surfaces.Mcp))
            .HandleAsync(new SearchProductsQuery("cafetera", "es", 1, 20), Ct);

        var tags = Assert.Single(heard.Of("tendero.search.requests"));
        Assert.Equal("agent", tags["actor.kind"]);
        Assert.Equal("mcp", tags["surface"]);
        Assert.Equal("query", tags["mode"]);
        Assert.Equal(false, tags["category"]);
        Assert.Equal("found", tags["result"]);
    }

    [Fact]
    public async Task A_category_page_is_a_browse_and_nothing_back_is_empty()
    {
        using var heard = new InstrumentRecorder("tendero.search.requests");

        await Handler(hits: 0, new Actor(ActorKinds.Human, Surfaces.Web))
            .HandleAsync(new SearchProductsQuery("", "es", 1, 20, Category: "KITCHEN"), Ct);

        var tags = Assert.Single(heard.Of("tendero.search.requests"));
        Assert.Equal("browse", tags["mode"]);
        Assert.Equal(true, tags["category"]);
        Assert.Equal("empty", tags["result"]);
    }

    private static SearchProductsHandler Handler(int hits, Actor actor) =>
        new(new Fixed(hits), new NoAnswer(), new GivenActor(actor));

    private sealed class Fixed(int hits) : ISearchStrategy
    {
        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(new SearchResultPage(
                [.. Enumerable.Range(0, hits).Select(i => new SearchHit(
                    $"p{i}", "0123456789", "name", "slug", null, null, $"v{i}", $"sku{i}", 1, 1, 1, "EUR", null, true, 1))],
                hits, query.Page, query.PageSize, 1));
    }

    private sealed class NoAnswer : ISearchAnswerer
    {
        public Task<SearchAnswer?> AnswerAsync(ProductSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult<SearchAnswer?>(null);
    }

    private sealed class GivenActor(Actor actor) : IActorAccessor
    {
        public Actor Current => actor;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SearchMetricsCollection
{
    public const string Name = "Search metrics";
}
