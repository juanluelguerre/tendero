using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Publishing is what makes a product visible: it is imported into Draft and
/// search only looks at Active. Without that step the storefront cannot return a
/// result, however correct everything else is.
///
/// A deterministic fake rather than NSubstitute: the repository has behaviour (it
/// saves, finds, counts saves) and a double with behaviour reads better than
/// three chained Returns() (docs/testing.md).
/// </summary>
public sealed class PublishProductTests
{
    private static readonly TestClock Clock = new();

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
        product.Publish(Clock);
        var repository = new InMemoryProductRepository(product);
        var handler = HandlerOver(repository, out var unitOfWork);

        var result = await handler.HandleAsync(new PublishProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(PublishOutcome.AlreadyActive, result.Outcome);
        Assert.Equal(ProductStatus.Active, product.Status);
        // Publishing twice is not an error, but it is not a change either:
        // calling Publish() again would emit another ProductUpserted and set the
        // indexing worker to work leaving the index exactly as it was.
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Publishing_an_archived_product_is_rejected()
    {
        var product = ADraftProduct();
        product.Archive(Clock);
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
        // The index is not written here: ProductUpserted is emitted, the outbox
        // delivers it and the worker projects (invariant 7). What this slice has
        // to guarantee is that the event goes out.
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
        return new PublishProductHandler(repository, unitOfWork, Clock);
    }

    private static Product ADraftProduct() =>
        Product.Create(Clock, 
            LocalizedText.From("es", "Cafetera italiana 12 tazas"),
            new Money(29.90m, "EUR"));

}
