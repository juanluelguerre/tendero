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

    [Fact]
    public async Task Reindexing_indexes_every_active_product()
    {
        var first = AnActiveProduct();
        var second = AnActiveProduct();
        var indexer = new RecordingIndexer();

        var result = await HandlerOver(indexer, first, second)
            .HandleAsync(new ReindexProductsCommand(), TestContext.Current.CancellationToken);

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
            .HandleAsync(new ReindexProductsCommand(), TestContext.Current.CancellationToken);

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
            .HandleAsync(new ReindexProductsCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Indexed);
        Assert.Equal(0, result.Removed);
    }

    private static ReindexProductsHandler HandlerOver(IProductIndexer indexer, params Product[] products) =>
        new(new InMemoryProductReader(products), indexer);

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

    private sealed class RecordingIndexer : IProductIndexer
    {
        public List<ProductId> Indexed { get; } = [];
        public List<ProductId> Removed { get; } = [];

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
