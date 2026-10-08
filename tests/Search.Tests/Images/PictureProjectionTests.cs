using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.ProjectProductToPictureVectors;
using ElGuerre.Tendero.Search.Features.ReindexProducts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The picture projection on the outbox (P13-3): the rule it keeps, what keeps it
/// from holding the lexical index hostage, and what a process says when it was
/// started without a store or without the model.
/// </summary>
public sealed class PictureProjectionTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- The projection handlers ----------

    [Fact]
    public async Task An_active_product_has_its_pictures_embedded_and_a_draft_has_them_removed()
    {
        var active = AProduct(publish: true);
        var draft = AProduct(publish: false);
        var pictures = new RecordingPictures();
        var products = new Products(active, draft);

        await Handler(products, pictures).HandleAsync(new ProductUpserted(active.Id, Clock.GetUtcNow()), Ct);
        await Handler(products, pictures).HandleAsync(new ProductUpserted(draft.Id, Clock.GetUtcNow()), Ct);

        Assert.Equal([active.Id], pictures.Indexed);
        Assert.Equal([draft.Id], pictures.Removed);
    }

    /// <summary>
    /// The outbox retries a whole message when one handler throws, and the lexical
    /// projection is another handler of the same message: pictures that cannot be
    /// embedded cost the pictures, never the index.
    /// </summary>
    [Fact]
    public async Task A_picture_layer_that_fails_does_not_fail_the_message()
    {
        var product = AProduct(publish: true);
        var pictures = new RecordingPictures { Failure = new SearchUnavailableException("embedding", "Qdrant does not answer") };

        await Handler(new Products(product), pictures).HandleAsync(new ProductUpserted(product.Id, Clock.GetUtcNow()), Ct);
        await new RemoveProductPicturesOnArchived(pictures, NullLogger<RemoveProductPicturesOnArchived>.Instance)
            .HandleAsync(new ProductArchived(product.Id, Clock.GetUtcNow()), Ct);

        Assert.Equal(2, pictures.Attempts);
    }

    /// <summary>A model that throws something of its own — a picture it cannot decode — is swallowed like the store's.</summary>
    [Fact]
    public async Task A_picture_the_model_cannot_read_does_not_fail_the_message_either()
    {
        var product = AProduct(publish: true);
        var pictures = new RecordingPictures { Failure = new InvalidOperationException("not a picture") };

        await Handler(new Products(product), pictures).HandleAsync(new ProductUpserted(product.Id, Clock.GetUtcNow()), Ct);

        Assert.Equal(1, pictures.Attempts);
    }

    [Fact]
    public async Task A_catalogue_that_does_not_answer_is_not_swallowed()
    {
        var handler = Handler(new Products { Failure = new InvalidOperationException("the catalogue is down") }, new RecordingPictures());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ProductUpserted(ProductId.New(), Clock.GetUtcNow()), Ct));
    }

    [Fact]
    public async Task Cancellation_is_not_mistaken_for_a_failure_of_the_layer()
    {
        var product = AProduct(publish: true);
        var pictures = new RecordingPictures { Failure = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Handler(new Products(product), pictures).HandleAsync(new ProductUpserted(product.Id, Clock.GetUtcNow()), Ct));
    }

    // ---------- Rebuilding an empty collection in the background ----------

    [Fact]
    public async Task A_process_without_a_picture_layer_stops_looking_at_once()
    {
        var (service, dispatcher) = Rebuilder(new NoPictureVectors());

        await RunAsync(service);

        Assert.Empty(dispatcher.Sent);
    }

    [Fact]
    public async Task An_empty_collection_is_rebuilt_and_a_populated_one_is_left_alone()
    {
        var (empty, rebuilt) = Rebuilder(new RecordingPictures { Empty = true });
        var (full, untouched) = Rebuilder(new RecordingPictures { Empty = false });

        await RunAsync(empty);
        await RunAsync(full);

        var command = Assert.IsType<ReindexProductsCommand>(Assert.Single(rebuilt.Sent));
        Assert.Equal(ReindexTargets.Pictures, command.Target);
        Assert.Empty(untouched.Sent);
    }

    /// <summary>
    /// Qdrant starts after the worker on a cold stack, and an exception out of a
    /// BackgroundService stops the host that drains the outbox.
    /// </summary>
    [Fact]
    public async Task A_store_that_does_not_answer_yet_is_asked_again_rather_than_stopping_the_worker()
    {
        var (service, dispatcher) = Rebuilder(new RecordingPictures { Empty = true, EmptinessFailuresLeft = 2 });

        await RunAsync(service);

        Assert.Single(dispatcher.Sent);
    }

    // ---------- The stand-in ----------

    [Fact]
    public async Task The_stand_in_writes_nothing_says_it_is_not_configured_and_refuses_to_search()
    {
        var none = new NoPictureVectors();

        await none.IndexAsync(AProduct(publish: true), Ct);
        await none.RemoveAsync(ProductId.New(), Ct);

        Assert.False(((IPictureVectorIndexer)none).IsConfigured);
        Assert.False(await none.IsEmptyAsync(Ct));
        var refusal = await Assert.ThrowsAsync<SearchStageNotConfiguredException>(() => none.NearestAsync(new float[4], 5, Ct));
        Assert.Contains(NoPictureVectors.Stage, refusal.Message, StringComparison.Ordinal);
    }

    // ---------- Composition ----------

    /// <summary>A store with no model has nothing to fill it, and a model with no store has nowhere to write.</summary>
    [Theory]
    [InlineData("Endpoint=http://127.0.0.1:1;Key=unit-test", null)]
    [InlineData(null, "models/siglip2")]
    [InlineData(null, null)]
    public async Task Without_both_a_store_and_a_model_the_process_gets_the_stand_in(string? qdrant, string? models)
    {
        using var provider = Compose(qdrant, models);
        using var scope = provider.CreateScope();

        var indexer = scope.ServiceProvider.GetRequiredService<IPictureVectorIndexer>();

        Assert.IsType<NoPictureVectors>(indexer);
        Assert.IsType<NoPictureVectors>(scope.ServiceProvider.GetRequiredService<IPictureSearch>());
        Assert.False(indexer.IsConfigured);
    }

    /// <summary>Read as descriptors: resolving the real adapter would ask for an image store this suite does not compose.</summary>
    [Fact]
    public void With_a_store_and_a_model_the_indexer_and_the_search_are_the_qdrant_adapter()
    {
        var services = new ServiceCollection().AddPictureVectors(Configuration("Endpoint=http://127.0.0.1:1;Key=unit-test", "models/siglip2"));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(QdrantPictureVectors));
        // The real one is registered first, so the stand-in's TryAdd never lands.
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPictureVectorIndexer));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPictureSearch));
    }

    /// <summary>The words' layer and the pictures' share one client: each registered alone, or both, there is exactly one.</summary>
    [Fact]
    public void The_words_and_the_pictures_share_one_qdrant_client()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = "Endpoint=http://127.0.0.1:1;Key=unit-test",
            ["services:embeddings:http:0"] = "http://127.0.0.1:1",
            [ImageEmbeddingServiceCollectionExtensions.FolderKey] = "models/siglip2",
        }).Build();

        var services = new ServiceCollection().AddLogging().AddVectorLayer(configuration).AddPictureVectors(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(global::Qdrant.Client.QdrantClient));
    }

    // ---------- Names ----------

    /// <summary>Never the words' collection: another model, another space, another name.</summary>
    [Fact]
    public void The_collection_is_named_for_the_picture_model_and_is_not_the_words()
    {
        var model = EmbeddingModelDescriptor.Siglip2Base;

        Assert.Equal("pictures_siglip2-base-patch16-224_75de2d5_768", PictureCollection.NameFor(model));
        Assert.NotEqual(model.CollectionName, PictureCollection.NameFor(model));
    }

    [Fact]
    public void A_picture_is_always_the_same_point_and_two_pictures_never_are()
    {
        var (one, another) = (new ImageId("sha256-aaaa"), new ImageId("sha256-bbbb"));

        Assert.Equal(PicturePointId.For(one), PicturePointId.For(new ImageId("sha256-aaaa")));
        Assert.NotEqual(PicturePointId.For(one), PicturePointId.For(another));
        Assert.Equal(8, PicturePointId.For(one).Version);
    }

    private static ProjectProductPicturesOnUpserted Handler(IProductReader products, IPictureVectorIndexer pictures) =>
        new(products, pictures, NullLogger<ProjectProductPicturesOnUpserted>.Instance);

    private static IConfiguration Configuration(string? qdrant, string? models) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = qdrant,
            [ImageEmbeddingServiceCollectionExtensions.FolderKey] = models,
        }).Build();

    private static ServiceProvider Compose(string? qdrant, string? models) =>
        new ServiceCollection()
            .AddLogging()
            .AddPictureVectors(Configuration(qdrant, models))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static (RebuildPictureVectorsAtStartupWorker Service, RecordingDispatcher Dispatcher) Rebuilder(IPictureVectorIndexer pictures)
    {
        var dispatcher = new RecordingDispatcher();

        var services = new ServiceCollection()
            .AddSingleton(pictures)
            .AddSingleton<ICommandDispatcher>(dispatcher)
            .BuildServiceProvider();

        return (new RebuildPictureVectorsAtStartupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<RebuildPictureVectorsAtStartupWorker>.Instance)
        { RetryEvery = TimeSpan.FromMilliseconds(5) }, dispatcher);
    }

    private static async Task RunAsync(RebuildPictureVectorsAtStartupWorker service)
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

    private sealed class RecordingPictures : IPictureVectorIndexer
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
                throw new SearchUnavailableException("counting picture vectors", "Qdrant is still starting");

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
