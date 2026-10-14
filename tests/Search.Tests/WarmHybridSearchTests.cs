using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The hybrid search is asked once in each language when the API starts, so the
/// first shopper is not the one whose search finds the embeddings and the
/// reranker cold, and degrades to lexical (phase 13c: the first kit asked of a
/// fresh stack chose another coffee maker than every kit after it).
/// </summary>
public sealed class WarmHybridSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Once_the_vector_layer_answers_the_search_is_asked_once_in_each_language()
    {
        var (layer, search) = (new Layer(), new Search());

        await RunAsync(layer, search);

        Assert.Equal(["es", "en"], search.Asked.Select(query => query.Culture));
    }

    /// <summary>The models load after the API starts: the worker waits for the layer to answer, and asks then.</summary>
    [Fact]
    public async Task A_layer_still_loading_is_waited_for()
    {
        var (layer, search) = (new Layer { UnavailableFor = 2 }, new Search());

        await RunAsync(layer, search);

        Assert.Equal((3, 2), (layer.Checks, search.Asked.Count));
    }

    [Fact]
    public async Task A_shop_without_the_vector_layer_asks_nothing()
    {
        var (layer, search) = (new Layer { Configured = false }, new Search());

        await RunAsync(layer, search);

        Assert.Empty(search.Asked);
    }

    /// <summary>A layer that never answers is given up on: warming is a courtesy, not a condition.</summary>
    [Fact]
    public async Task A_layer_that_never_answers_is_given_up_on()
    {
        var (layer, search) = (new Layer { UnavailableFor = int.MaxValue }, new Search());

        await RunAsync(layer, search, attempts: 4);

        Assert.Equal((4, 0), (layer.Checks, search.Asked.Count));
    }

    /// <summary>An exception out of a BackgroundService stops the host: a search that fails costs nothing but the warming.</summary>
    [Fact]
    public async Task A_search_that_fails_does_not_stop_the_host_nor_the_other_language()
    {
        var (layer, search) = (new Layer(), new Search { FailIn = "es" });

        await RunAsync(layer, search);

        Assert.Equal(["es", "en"], search.Asked.Select(query => query.Culture));
    }

    private static async Task RunAsync(Layer layer, Search search, int attempts = 24)
    {
        var services = new ServiceCollection();
        services.AddScoped<IVectorLayer>(_ => layer);
        services.AddKeyedScoped<ISearchStrategy>(SearchStrategies.Hybrid, (_, _) => search);
        await using var provider = services.BuildServiceProvider();

        var worker = new WarmHybridSearchAtStartupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WarmHybridSearchAtStartupWorker>.Instance,
            TimeProvider.System, every: TimeSpan.Zero, attempts: attempts);

        await worker.StartAsync(Ct);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private sealed class Layer : IVectorLayer
    {
        public bool Configured { get; init; } = true;

        public int UnavailableFor { get; init; }

        public int Checks { get; private set; }

        public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return Task.FromResult(
                !Configured ? VectorLayerStatus.NotConfigured
                : Checks <= UnavailableFor ? VectorLayerStatus.Unavailable("loading")
                : VectorLayerStatus.Available("ready"));
        }
    }

    private sealed class Search : ISearchStrategy
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        public string? FailIn { get; init; }

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);
            if (query.Culture == FailIn)
                throw new HttpRequestException("the reranker went away");
            return Task.FromResult(new SearchResultPage([], 0, 1, query.PageSize, 0));
        }
    }
}
