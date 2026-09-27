using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.WithdrawProduct;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Withdrawing takes a live product back to the review queue, the inverse of
/// publishing. Serial with PublishProductTests, because both listen to static
/// counters on the catalogue's meter.
/// </summary>
[Collection(PublishMetricsCollection.Name)]
public sealed class WithdrawProductTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Withdrawing_an_active_product_puts_it_back_in_draft()
    {
        var product = AnActiveProduct();
        var handler = HandlerOver(new InMemoryProductRepository(product), out var unitOfWork);

        var result = await handler.HandleAsync(new WithdrawProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(WithdrawOutcome.Withdrawn, result.Outcome);
        Assert.Equal("draft", result.Status);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Withdrawing_raises_the_event_that_takes_it_out_of_the_index()
    {
        // The projection removes a product that is no longer Active on
        // ProductUpserted; what this slice owes it is the event.
        var product = AnActiveProduct();
        product.ClearDomainEvents();
        var handler = HandlerOver(new InMemoryProductRepository(product), out _);

        await handler.HandleAsync(new WithdrawProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Contains(product.DomainEvents, e => e is ProductUpserted);
    }

    [Fact]
    public async Task Withdrawing_a_draft_saves_nothing()
    {
        var product = ADraftProduct();
        product.ClearDomainEvents();
        var handler = HandlerOver(new InMemoryProductRepository(product), out var unitOfWork);

        var result = await handler.HandleAsync(new WithdrawProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(WithdrawOutcome.AlreadyDraft, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task Withdrawing_an_archived_product_is_refused()
    {
        var product = AnActiveProduct();
        product.Archive(Clock);
        var handler = HandlerOver(new InMemoryProductRepository(product), out var unitOfWork);

        var result = await handler.HandleAsync(new WithdrawProductCommand(product.Id), TestContext.Current.CancellationToken);

        Assert.Equal(WithdrawOutcome.Archived, result.Outcome);
        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Withdrawing_a_product_that_does_not_exist_reports_not_found()
    {
        var handler = HandlerOver(new InMemoryProductRepository(), out var unitOfWork);

        var result = await handler.HandleAsync(new WithdrawProductCommand(ProductId.New()), TestContext.Current.CancellationToken);

        Assert.Equal(WithdrawOutcome.NotFound, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>
    /// `tendero.catalog.products.withdrawn`: a real Active → Draft counts, by
    /// who did it. Three live products and one draft, so a counter that counted
    /// the wrong calls could not land on the right number by accident.
    /// </summary>
    [Fact]
    public async Task Only_a_real_withdrawal_is_counted_and_by_whom()
    {
        using var heard = new InstrumentRecorder("tendero.catalog.products.withdrawn");
        Product[] live = [AnActiveProduct(), AnActiveProduct(), AnActiveProduct()];
        var draft = ADraftProduct();
        var handler = new WithdrawProductHandler(
            new InMemoryProductRepository([.. live, draft]), new CountingUnitOfWork(), Clock,
            new GivenActor(new Actor(ActorKinds.Human, Surfaces.Web)));

        foreach (var product in live.Append(draft))
            await handler.HandleAsync(new WithdrawProductCommand(product.Id), TestContext.Current.CancellationToken);

        var measurements = heard.Of("tendero.catalog.products.withdrawn");
        Assert.Equal(3, measurements.Count);
        Assert.All(measurements, tags => Assert.Equal("human", tags["actor.kind"]));
    }

    [Fact]
    public void Only_an_active_product_can_be_withdrawn()
    {
        Assert.Throws<InvalidOperationException>(() => ADraftProduct().Withdraw(Clock));
    }

    private static WithdrawProductHandler HandlerOver(
        InMemoryProductRepository repository, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new WithdrawProductHandler(repository, unitOfWork, Clock, new SystemActorAccessor());
    }

    private static Product ADraftProduct() =>
        Product.Create(Clock, LocalizedText.From("es", "Cafetera italiana 12 tazas"), new Money(29.90m, "EUR"));

    private static Product AnActiveProduct()
    {
        var product = ADraftProduct();
        product.Publish(Clock);
        return product;
    }
}
