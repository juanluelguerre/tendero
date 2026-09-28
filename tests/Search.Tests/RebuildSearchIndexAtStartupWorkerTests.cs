using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.ReindexProducts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The shop after Docker restarts.
///
/// Elasticsearch runs with no volume on purpose (ADR 0012) and Postgres keeps
/// its data, so recreating the container leaves a hundred Active products and an
/// empty index. The UI review of 2026-09-11 met exactly that: the storefront
/// answered its own suggested query with "Nothing matched — try fewer words",
/// the home page hid its new arrivals, and the only way back was a POST nobody
/// has a button for. `ReindexProducts` had described the case in its own
/// comment — "nothing restores it" — for a week.
/// </summary>
public sealed class RebuildSearchIndexAtStartupWorkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_empty_index_is_rebuilt_when_the_worker_starts()
    {
        var (service, dispatcher) = Build(new ProbingIndexer(empty: true));

        await service.StartAsync(Ct);

        Assert.IsType<ReindexProductsCommand>(Assert.Single(dispatcher.Sent));
    }

    /// <summary>A populated index is left alone: rebuilding on every start would
    /// make a restart cost a walk of the whole catalogue for nothing.</summary>
    [Fact]
    public async Task A_populated_index_is_left_alone()
    {
        var (service, dispatcher) = Build(new ProbingIndexer(empty: false));

        await service.StartAsync(Ct);

        Assert.Empty(dispatcher.Sent);
    }

    /// <summary>
    /// A search engine that does not answer must not stop the worker: the same
    /// process drains the outbox that captures payments and holds stock, and a
    /// shop that cannot take orders because its index is down has made lexical
    /// search the one thing everything depends on.
    /// </summary>
    [Fact]
    public async Task A_search_engine_that_does_not_answer_does_not_stop_the_worker()
    {
        var (service, dispatcher) = Build(new ProbingIndexer(empty: true, unreachable: true));

        await service.StartAsync(Ct);

        Assert.Empty(dispatcher.Sent);
    }

    /// <summary>
    /// An index on an older mapping is recreated and refilled even though it is
    /// full (R-7.3, F-1): its new fields were mapped dynamically, and a filter on
    /// them would fail without a word.
    /// </summary>
    [Fact]
    public async Task A_populated_index_on_an_older_mapping_is_recreated()
    {
        var (service, dispatcher) = Build(new ProbingIndexer(empty: false), new FixedSchema(current: false));

        await service.StartAsync(Ct);

        Assert.True(Assert.IsType<ReindexProductsCommand>(Assert.Single(dispatcher.Sent)).Recreate);
    }

    [Fact]
    public async Task An_empty_index_on_the_current_mapping_is_refilled_without_recreating()
    {
        var (service, dispatcher) = Build(new ProbingIndexer(empty: true), new FixedSchema(current: true));

        await service.StartAsync(Ct);

        Assert.False(Assert.IsType<ReindexProductsCommand>(Assert.Single(dispatcher.Sent)).Recreate);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("2", 2)]
    [InlineData("not a number", 1)]
    public void A_mapping_says_its_version_or_is_the_first(string? stored, int version)
    {
        var meta = stored is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["mapping_version"] = stored };

        Assert.Equal(version, ElasticsearchIndexSchema.VersionOf(meta));
    }

    private static (RebuildSearchIndexAtStartupWorker Service, RecordingDispatcher Dispatcher) Build(IProductIndexer indexer, ISearchIndexSchema? schema = null)
    {
        var dispatcher = new RecordingDispatcher();

        var collection = new ServiceCollection()
            .AddSingleton(indexer)
            .AddSingleton<ICommandDispatcher>(dispatcher);
        if (schema is not null)
            collection.AddSingleton(schema);
        var services = collection.BuildServiceProvider();

        return (new RebuildSearchIndexAtStartupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RebuildSearchIndexAtStartupWorker>.Instance), dispatcher);
    }

    private sealed class ProbingIndexer(bool empty, bool unreachable = false) : IProductIndexer
    {
        public Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
            unreachable
                ? throw new SearchUnavailableException("count", "connection refused")
                : Task.FromResult(empty);

        public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedSchema(bool current) : ISearchIndexSchema
    {
        public Task<bool> IsCurrentAsync(CancellationToken ct = default) => Task.FromResult(current);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public List<object> Sent { get; } = [];

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            Sent.Add(command);
            return Task.FromResult((TResult)(object)new ReindexProductsResult(Indexed: 0, Removed: 0, ElapsedSeconds: 0));
        }
    }
}
