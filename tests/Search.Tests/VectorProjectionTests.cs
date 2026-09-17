using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.ProjectProductToVectors;
using ElGuerre.Tendero.Search.Features.ReindexProducts;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The vector projection on the outbox, and what keeps it from ever holding the
/// lexical index hostage (invariant 8 on the write path).
/// </summary>
public sealed class VectorProjectionTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- The projection handlers ----------

    [Fact]
    public async Task An_active_product_is_embedded_and_a_draft_is_removed()
    {
        var active = AProduct(publish: true);
        var draft = AProduct(publish: false);
        var vectors = new RecordingVectors();
        var products = new Products(active, draft);

        await Handler(products, vectors).HandleAsync(new ProductUpserted(active.Id, Clock.GetUtcNow()), Ct);
        await Handler(products, vectors).HandleAsync(new ProductUpserted(draft.Id, Clock.GetUtcNow()), Ct);

        Assert.Equal([active.Id], vectors.Indexed);
        Assert.Equal([draft.Id], vectors.Removed);
    }

    /// <summary>
    /// The outbox retries a whole message when one handler throws, and the lexical
    /// projection is another handler of the same message. A vector layer that is
    /// down must cost the vectors, not the index.
    /// </summary>
    [Fact]
    public async Task A_vector_layer_that_fails_does_not_fail_the_message()
    {
        var product = AProduct(publish: true);
        var vectors = new RecordingVectors { Failure = new SearchUnavailableException("embedding", "the model server does not answer") };

        await Handler(new Products(product), vectors).HandleAsync(new ProductUpserted(product.Id, Clock.GetUtcNow()), Ct);
        await new RemoveProductVectorsOnArchived(vectors, NullLogger<RemoveProductVectorsOnArchived>.Instance)
            .HandleAsync(new ProductArchived(product.Id, Clock.GetUtcNow()), Ct);

        Assert.Equal(2, vectors.Attempts);
    }

    /// <summary>
    /// The guard is around the vector layer and nothing else: a catalogue that
    /// does not answer is not a vector failure, and swallowing it would mark the
    /// message processed with nothing projected anywhere.
    /// </summary>
    [Fact]
    public async Task A_catalogue_that_does_not_answer_is_not_swallowed()
    {
        var handler = Handler(new Products { Failure = new InvalidOperationException("the catalogue is down") }, new RecordingVectors());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ProductUpserted(ProductId.New(), Clock.GetUtcNow()), Ct));
    }

    [Fact]
    public async Task Cancellation_is_not_mistaken_for_a_failure_of_the_layer()
    {
        var product = AProduct(publish: true);
        var vectors = new RecordingVectors { Failure = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Handler(new Products(product), vectors).HandleAsync(new ProductUpserted(product.Id, Clock.GetUtcNow()), Ct));
    }

    // ---------- Rebuilding an empty collection in the background ----------

    [Fact]
    public async Task A_process_without_a_vector_layer_stops_looking_at_once()
    {
        var (service, dispatcher) = Rebuilder(new ScriptedLayer(VectorLayerStatus.NotConfigured), new RecordingVectors { Empty = true });

        await RunAsync(service);

        Assert.Empty(dispatcher.Sent);
    }

    /// <summary>
    /// The first start of the model server downloads its weights for minutes, and
    /// nothing waits for it. The rebuild waits instead — and then does its job.
    /// </summary>
    [Fact]
    public async Task An_empty_collection_is_rebuilt_once_the_layer_answers()
    {
        var layer = new ScriptedLayer(
            VectorLayerStatus.Unavailable("downloading"),
            VectorLayerStatus.Unavailable("downloading"),
            VectorLayerStatus.Available("bge-m3"));
        var (service, dispatcher) = Rebuilder(layer, new RecordingVectors { Empty = true });

        await RunAsync(service);

        var command = Assert.IsType<ReindexProductsCommand>(Assert.Single(dispatcher.Sent));
        Assert.Equal(ReindexTargets.Vector, command.Target);
        Assert.Equal(3, layer.Checks);
    }

    [Fact]
    public async Task A_populated_collection_is_left_alone()
    {
        var (service, dispatcher) = Rebuilder(new ScriptedLayer(VectorLayerStatus.Available("bge-m3")), new RecordingVectors { Empty = false });

        await RunAsync(service);

        Assert.Empty(dispatcher.Sent);
    }

    /// <summary>
    /// An exception out of a BackgroundService stops the host, and this host
    /// drains the outbox that takes the money.
    /// </summary>
    [Fact]
    public async Task A_failure_is_tried_again_rather_than_stopping_the_worker()
    {
        var vectors = new RecordingVectors { Empty = true, EmptinessFailuresLeft = 1 };
        var (service, dispatcher) = Rebuilder(new ScriptedLayer(VectorLayerStatus.Available("bge-m3")), vectors);

        await RunAsync(service);

        Assert.Single(dispatcher.Sent);
    }

    // ---------- Composition ----------

    [Fact]
    public async Task A_process_given_no_vector_layer_gets_one_that_says_so()
    {
        using var provider = Compose(new Dictionary<string, string?>
        {
            // A model server alone is not a vector layer: there is nowhere to write.
            ["services:embeddings:http:0"] = "http://127.0.0.1:1"
        });
        using var scope = provider.CreateScope();

        var status = await scope.ServiceProvider.GetRequiredService<IVectorLayer>().CheckAsync(Ct);

        Assert.False(status.IsConfigured);
        await scope.ServiceProvider.GetRequiredService<IProductVectorIndexer>().IndexAsync(AProduct(publish: true), Ct);
        await Assert.ThrowsAsync<SearchStageNotConfiguredException>(() =>
            scope.ServiceProvider.GetRequiredService<IVectorProductSearch>().SearchAsync("cafetera", "es", null, 10, Ct));
    }

    [Fact]
    public void A_process_given_qdrant_and_a_model_server_gets_the_real_layer_without_connecting()
    {
        using var provider = Compose(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = "Endpoint=http://127.0.0.1:1;Key=unit-test",
            ["services:embeddings:http:0"] = "http://127.0.0.1:1"
        });

        Assert.IsType<QdrantVectorLayer>(provider.GetRequiredService<IVectorLayer>());
    }

    private static ProjectProductVectorsOnUpserted Handler(IProductReader products, IProductVectorIndexer vectors) =>
        new(products, vectors, NullLogger<ProjectProductVectorsOnUpserted>.Instance);

    private static ServiceProvider Compose(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddLogging()
            .AddVectorLayer(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static (RebuildEmptyVectorIndex Service, RecordingDispatcher Dispatcher) Rebuilder(
        IVectorLayer layer, IProductVectorIndexer vectors)
    {
        var dispatcher = new RecordingDispatcher();

        var services = new ServiceCollection()
            .AddSingleton(layer)
            .AddSingleton(vectors)
            .AddSingleton<ICommandDispatcher>(dispatcher)
            .BuildServiceProvider();

        return (new RebuildEmptyVectorIndex(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<RebuildEmptyVectorIndex>.Instance)
        { RetryEvery = TimeSpan.FromMilliseconds(5) }, dispatcher);
    }

    private static async Task RunAsync(RebuildEmptyVectorIndex service)
    {
        await service.StartAsync(Ct);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private static Product AProduct(bool publish)
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera").With("en", "Coffee maker"), new Money(29.90m, "EUR"));
        product.AddVariant(Clock, "COFFEE-DEFAULT", new Money(29.90m, "EUR"));

        if (publish)
            product.Publish(Clock);

        return product;
    }

    private sealed class Products(params Product[] products) : IProductReader
    {
        public Exception? Failure { get; init; }

        public Task<Product?> FindBySkuAsync(string sku, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
            Failure is not null ? throw Failure : Task.FromResult(products.SingleOrDefault(product => product.Id == id));

        public IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingVectors : IProductVectorIndexer
    {
        public List<ProductId> Indexed { get; } = [];
        public List<ProductId> Removed { get; } = [];
        public Exception? Failure { get; init; }
        public int Attempts { get; private set; }
        public bool Empty { get; init; }
        public int EmptinessFailuresLeft { get; set; }

        public Task IndexAsync(Product product, CancellationToken ct = default) => Record(Indexed, product.Id);

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Record(Removed, productId);

        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsEmptyAsync(CancellationToken ct = default)
        {
            if (EmptinessFailuresLeft-- > 0)
                throw new SearchUnavailableException("counting vectors", "Qdrant restarted");

            return Task.FromResult(Empty);
        }

        private Task Record(List<ProductId> into, ProductId id)
        {
            Attempts++;

            if (Failure is not null)
                throw Failure;

            into.Add(id);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedLayer(params VectorLayerStatus[] answers) : IVectorLayer
    {
        public int Checks { get; private set; }

        public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult(answers[Math.Min(Checks++, answers.Length - 1)]);
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
