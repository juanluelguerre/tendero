using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.ReindexProducts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The index is a disposable projection, and architecture.md promises it can be
/// rebuilt from Postgres. Until now that promise had no implementation, and when
/// the Elasticsearch container was recreated, a whole catalogue in Active became
/// invisible with no way to reconcile it: the outbox had already delivered its
/// events, and publishing again changes nothing.
///
/// Deterministic fakes: an indexer that remembers what it was sent reads better
/// than assertions over a mock (docs/testing.md).
/// </summary>
public sealed class ReindexProductsTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reindexing_indexes_every_active_product()
    {
        var first = AnActiveProduct();
        var second = AnActiveProduct();
        var indexer = new RecordingIndexer();

        var result = await HandlerOver(indexer, first, second)
            .HandleAsync(new ReindexProductsCommand(), Ct);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(0, result.Removed);
        Assert.Equal([first.Id, second.Id], indexer.Indexed);
    }

    [Fact]
    public async Task Reindexing_removes_what_is_no_longer_active()
    {
        // Converge, not merely add: if a product stopped being Active, the
        // reindex has to take it out, or the index keeps a stale document that
        // search would go on returning.
        var active = AnActiveProduct();
        var draft = ADraftProduct();
        var archived = AnActiveProduct();
        archived.Archive(Clock);
        var indexer = new RecordingIndexer();

        var result = await HandlerOver(indexer, active, draft, archived)
            .HandleAsync(new ReindexProductsCommand(), Ct);

        Assert.Equal(1, result.Indexed);
        Assert.Equal(2, result.Removed);
        Assert.Equal([active.Id], indexer.Indexed);
        Assert.Equal([draft.Id, archived.Id], indexer.Removed);
    }

    [Fact]
    public async Task Reindexing_an_empty_catalogue_is_not_an_error()
    {
        var indexer = new RecordingIndexer();

        var result = await HandlerOver(indexer)
            .HandleAsync(new ReindexProductsCommand(), Ct);

        Assert.Equal(0, result.Indexed);
        Assert.Equal(0, result.Removed);
    }

    // ---------- Which projection (P8-4) ----------

    /// <summary>
    /// The endpoint rebuilt the lexical index before the vectors existed, and a
    /// caller who says nothing still gets exactly that — not a request that now
    /// waits for a model server.
    /// </summary>
    [Fact]
    public async Task The_lexical_index_is_the_default_and_the_vectors_are_left_alone()
    {
        var product = AnActiveProduct();
        var (lexical, vectors) = (new RecordingIndexer(), new RecordingVectors());

        await new ReindexProductsHandler(new InMemoryProductReader(product), lexical, vectors, Layer(available: true))
            .HandleAsync(new ReindexProductsCommand(), Ct);

        Assert.Equal([product.Id], lexical.Indexed);
        Assert.Empty(vectors.Indexed);
    }

    [Fact]
    public async Task The_vector_target_embeds_and_leaves_the_lexical_index_alone()
    {
        var product = AnActiveProduct();
        var draft = ADraftProduct();
        var (lexical, vectors) = (new RecordingIndexer(), new RecordingVectors());

        var result = await new ReindexProductsHandler(new InMemoryProductReader(product, draft), lexical, vectors, Layer(available: true))
            .HandleAsync(new ReindexProductsCommand(Target: ReindexTargets.Vector), Ct);

        Assert.Equal((1, 1), (result.Indexed, result.Removed));
        Assert.Equal([product.Id], vectors.Indexed);
        Assert.Equal([draft.Id], vectors.Removed);
        Assert.Empty(lexical.Indexed);
        Assert.Empty(lexical.Removed);
    }

    [Fact]
    public async Task All_rebuilds_both_from_one_walk_and_counts_each_product_once()
    {
        var product = AnActiveProduct();
        var (lexical, vectors) = (new RecordingIndexer(), new RecordingVectors());

        var result = await new ReindexProductsHandler(new InMemoryProductReader(product), lexical, vectors, Layer(available: true))
            .HandleAsync(new ReindexProductsCommand(Recreate: true, Target: ReindexTargets.All), Ct);

        Assert.Equal(1, result.Indexed);
        Assert.Equal([product.Id], lexical.Indexed);
        Assert.Equal([product.Id], vectors.Indexed);
        Assert.True(lexical.Recreated);
        Assert.True(vectors.Recreated);
    }

    /// <summary>
    /// The outbox swallows a vector failure on purpose; a person who asked for the
    /// rebuild is owed the reason instead of a count of products silently skipped.
    /// </summary>
    [Fact]
    public async Task Rebuilding_vectors_that_are_unavailable_says_why_and_touches_nothing()
    {
        var product = AnActiveProduct();
        var (lexical, vectors) = (new RecordingIndexer(), new RecordingVectors());

        var refusal = await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            new ReindexProductsHandler(new InMemoryProductReader(product), lexical, vectors, Layer(available: false))
                .HandleAsync(new ReindexProductsCommand(Target: ReindexTargets.All), Ct));

        Assert.Contains("the model server is downloading", refusal.Message);
        Assert.Empty(lexical.Indexed);
        Assert.Empty(vectors.Indexed);
    }

    /// <summary>
    /// A collection of the wrong size is exactly what makes the layer
    /// unavailable, and recreating it is the repair — so that one request is let
    /// through, and a layer nobody configured is still refused.
    /// </summary>
    [Fact]
    public async Task Recreating_is_the_repair_for_a_layer_that_is_configured_and_unavailable()
    {
        var product = AnActiveProduct();
        var vectors = new RecordingVectors();

        await new ReindexProductsHandler(new InMemoryProductReader(product), new RecordingIndexer(), vectors, Layer(available: false))
            .HandleAsync(new ReindexProductsCommand(Recreate: true, Target: ReindexTargets.Vector), Ct);

        Assert.True(vectors.Recreated);

        await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            new ReindexProductsHandler(new InMemoryProductReader(product), new RecordingIndexer(), new RecordingVectors(),
                    new FixedLayer(VectorLayerStatus.NotConfigured))
                .HandleAsync(new ReindexProductsCommand(Recreate: true, Target: ReindexTargets.Vector), Ct));
    }

    [Theory]
    [InlineData("lexical", true)]
    [InlineData("vector", true)]
    [InlineData("all", true)]
    [InlineData("Vector", false)]
    [InlineData("qdrant", false)]
    [InlineData("", false)]
    public void Only_the_three_targets_pass_the_door(string target, bool valid)
    {
        var result = new ReindexProductsValidator().Validate(new ReindexProductsCommand(Target: target));

        Assert.Equal(valid, result.IsValid);
    }

    private static ReindexProductsHandler HandlerOver(IProductIndexer indexer, params Product[] products) =>
        new(new InMemoryProductReader(products), indexer, new RecordingVectors(), new FixedLayer(VectorLayerStatus.NotConfigured));

    private static FixedLayer Layer(bool available) =>
        new(available
            ? VectorLayerStatus.Available("bge-m3 in products_bge-m3_5617a9f_1024")
            : VectorLayerStatus.Unavailable("the model server is downloading its weights"));

    private static Product ADraftProduct() =>
        Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));

    private static Product AnActiveProduct()
    {
        var product = ADraftProduct();
        product.Publish(Clock);
        return product;
    }

    private sealed class InMemoryProductReader(params Product[] products) : IProductReader
    {
        public Task<Product?> FindBySkuAsync(string sku, CancellationToken ct) =>
            Task.FromResult(products.FirstOrDefault(
                product => product.Variants.Any(variant => variant.Sku == sku)));

        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
            Task.FromResult(products.SingleOrDefault(p => p.Id == id));

        public async IAsyncEnumerable<Product> StreamAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var product in products)
            {
                ct.ThrowIfCancellationRequested();
                yield return product;
                await Task.Yield();
            }
        }
    }

    private sealed class FixedLayer(VectorLayerStatus status) : IVectorLayer
    {
        public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default) => Task.FromResult(status);
    }

    private sealed class RecordingVectors : RecordingIndexer, IProductVectorIndexer;

    private class RecordingIndexer : IProductIndexer
    {
        public List<ProductId> Indexed { get; } = [];
        public List<ProductId> Removed { get; } = [];

        /// <summary>Recorded rather than performed: this double has no mapping,
        /// so the only thing worth asserting is that the command asked.</summary>
        public bool Recreated { get; private set; }

        public Task RecreateAsync(CancellationToken ct = default)
        {
            Recreated = true;
            return Task.CompletedTask;
        }

        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task IndexAsync(Product product, CancellationToken ct = default)
        {
            Indexed.Add(product.Id);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default)
        {
            Removed.Add(productId);
            return Task.CompletedTask;
        }
    }
}
