using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.PublishImport;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Publishing what one migration brought, in one operation (phase 16, P16-12;
/// spec decision 8; ADR 0012, amended): a person who has read the import's
/// answer publishes its products at once instead of one by one. The import
/// still never publishes. Only what the run created, and only what nobody has
/// decided on since: a product that was published and withdrawn is in draft
/// because a person put it there, and stays.
/// </summary>
public sealed class PublishImportTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Product AProduct() =>
        Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));

    [Fact]
    public async Task What_a_run_brought_is_published_at_once_and_saved_once()
    {
        var (one, other) = (AProduct(), AProduct());
        var run = ImportRun.Start("shopify", Clock.GetUtcNow());
        run.Brought(one.Id);
        run.Brought(other.Id);
        var unitOfWork = new CountingUnitOfWork();

        var result = await Handler(run, unitOfWork, one, other).HandleAsync(new PublishImportCommand(run.Id), Ct);

        Assert.Equal((PublishImportOutcome.Published, 2), (result.Outcome, result.Published));
        Assert.All([one, other], product => Assert.Equal(ProductStatus.Active, product.Status));
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>
    /// Already on sale is nothing to do; withdrawn is a person's decision; archived
    /// is out of the catalogue. Each is counted, none is touched.
    /// </summary>
    [Fact]
    public async Task What_a_person_has_decided_on_since_is_left_as_it_is()
    {
        var (active, withdrawn, archived, draft) = (AProduct(), AProduct(), AProduct(), AProduct());
        active.Publish(Clock);
        withdrawn.Publish(Clock);
        withdrawn.Withdraw(Clock);
        archived.Archive(Clock);
        var run = ImportRun.Start("shopify", Clock.GetUtcNow());
        foreach (var product in new[] { active, withdrawn, archived, draft })
            run.Brought(product.Id);

        var result = await Handler(run, new CountingUnitOfWork(), active, withdrawn, archived, draft)
            .HandleAsync(new PublishImportCommand(run.Id), Ct);

        Assert.Equal((1, 1, 1, 1), (result.Published, result.AlreadyActive, result.Withdrawn, result.Archived));
        Assert.Equal(ProductStatus.Draft, withdrawn.Status);
    }

    [Fact]
    public async Task A_run_nobody_made_is_not_found()
    {
        var result = await Handler(ImportRun.Start("shopify", Clock.GetUtcNow()), new CountingUnitOfWork())
            .HandleAsync(new PublishImportCommand(ImportRunId.New()), Ct);

        Assert.Equal(PublishImportOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void A_product_remembers_when_it_was_first_published()
    {
        var clock = new TestClock();
        var product = AProduct();
        product.Publish(clock);
        var first = product.FirstPublishedAt;
        clock.Advance(TimeSpan.FromDays(1));
        product.Withdraw(clock);
        product.Publish(clock);

        Assert.NotNull(first);
        Assert.Equal(first, product.FirstPublishedAt);
    }

    private static PublishImportHandler Handler(ImportRun run, CountingUnitOfWork unitOfWork, params Product[] products) =>
        new(new OneRun(run), new InMemoryProductRepository(products), unitOfWork, Clock, new SystemActorAccessor());

    private sealed class OneRun(ImportRun run) : IImportRunRepository
    {
        public void Add(ImportRun added) => throw new NotSupportedException();

        public Task<ImportRun?> FindAsync(ImportRunId id, CancellationToken ct) =>
            Task.FromResult(id == run.Id ? run : null);
    }
}
