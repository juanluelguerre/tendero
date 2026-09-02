using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.ReindexProducts;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// El índice es una proyección desechable y architecture.md promete poder
/// rehacerlo desde Postgres. Hasta ahora esa promesa no tenía implementación, y
/// cuando el contenedor de Elasticsearch se recreó, un catálogo entero en Active
/// quedó invisible sin forma de reconciliarlo: el outbox ya había entregado sus
/// eventos y publicar de nuevo no cambia nada.
///
/// Fakes deterministas: un indexador que recuerda lo que le mandaron se lee
/// mejor que aserciones sobre un mock (docs/testing.md).
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
        // Converger, no solo añadir: si un producto dejó de estar Active, el
        // reindexado tiene que sacarlo, o el indice conserva un documento rancio
        // que la busqueda seguiria devolviendo.
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
