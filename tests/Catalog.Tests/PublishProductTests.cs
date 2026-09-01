using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Publicar es lo que hace visible un producto: se importa en Draft y la
/// búsqueda sólo mira lo Active. Sin este paso el storefront no puede devolver
/// un resultado, por correcto que sea todo lo demás.
///
/// Fake determinista en vez de NSubstitute: el repositorio tiene comportamiento
/// (guarda, encuentra, cuenta guardados) y un doble con comportamiento se lee
/// mejor que tres Returns() encadenados (docs/testing.md).
/// </summary>
public sealed class PublishProductTests
{
    [Fact]
    public async Task Publishing_a_draft_product_makes_it_active()
    {
        var product = ADraftProduct();
        var repository = new InMemoryProductRepository(product);
        var handler = HandlerOver(repository, out var unitOfWork);

        var result = await handler.HandleAsync(new PublishProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Publishing_a_product_that_is_already_active_saves_nothing()
    {
        var product = ADraftProduct();
        product.Publish();
        var repository = new InMemoryProductRepository(product);
        var handler = HandlerOver(repository, out var unitOfWork);

        var result = await handler.HandleAsync(new PublishProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(PublishOutcome.AlreadyActive, result.Outcome);
        Assert.Equal(ProductStatus.Active, product.Status);
        // Publicar dos veces no es un error, pero tampoco es un cambio: volver a
        // llamar a Publish() emitiría otro ProductUpserted y haría trabajar al
        // worker de indexación para dejar el índice exactamente como estaba.
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Publishing_an_archived_product_is_rejected()
    {
        var product = ADraftProduct();
        product.Archive();
        var repository = new InMemoryProductRepository(product);
        var handler = HandlerOver(repository, out var unitOfWork);

        var result = await handler.HandleAsync(new PublishProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(PublishOutcome.Archived, result.Outcome);
        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Publishing_a_product_that_does_not_exist_reports_not_found()
    {
        var repository = new InMemoryProductRepository();
        var handler = HandlerOver(repository, out var unitOfWork);

        var result = await handler.HandleAsync(new PublishProductCommand(ProductId.New()), TestContext.Current.CancellationToken);

        Assert.Equal(PublishOutcome.NotFound, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Publishing_raises_the_event_the_indexer_listens_to()
    {
        // El índice no se escribe aquí: se emite ProductUpserted, el outbox lo
        // entrega y el worker proyecta (invariante 7). Lo que este slice debe
        // garantizar es que el evento sale.
        var product = ADraftProduct();
        product.ClearDomainEvents();
        var repository = new InMemoryProductRepository(product);
        var handler = HandlerOver(repository, out _);

        await handler.HandleAsync(new PublishProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Contains(product.DomainEvents, e => e is ProductUpserted);
    }

    private static PublishProductHandler HandlerOver(
        InMemoryProductRepository repository, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new PublishProductHandler(repository, unitOfWork);
    }

    private static Product ADraftProduct() =>
        Product.Create(
            LocalizedText.From("es", "Cafetera italiana 12 tazas"),
            new Money(29.90m, "EUR"));

    private sealed class InMemoryProductRepository(params Product[] products) : IProductRepository
    {
        private readonly List<Product> _products = [.. products];

        public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
            Task.FromResult(_products.SingleOrDefault(p => p.Id == id));

        public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
            Task.FromResult(_products.SingleOrDefault(
                p => p.ExternalReferences.Any(r => r.Source == source && r.ExternalId == externalId)));

        public void Add(Product product) => _products.Add(product);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
