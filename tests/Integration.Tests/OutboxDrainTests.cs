using System.Text.Json;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Import -> outbox -> drain -> projection, against a real Postgres.
///
/// Every step already had unit tests and none of them covered what breaks here:
/// that the event drain happens INSIDE the transaction that caused it, that the
/// jsonb converters survive a round trip, that the unique index on
/// <c>(source, external_id)</c> makes re-importing idempotent, and that the
/// message deserialised from the outbox reaches its handler.
///
/// Elasticsearch takes no part: the indexer is an in-memory double. What is
/// measured is the outbox mechanism, not the search engine — the SearchEval gate
/// already covers that one, with a real Elasticsearch.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxDrainTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    /// <summary>
    /// How many products the seed file holds, READ FROM THE FILE.
    ///
    /// It was the constant `6`, and the catalogue growing to a hundred turned
    /// three green tests red for a reason that had nothing to do with the outbox.
    /// A test that hard-codes the size of its fixture is asserting the fixture,
    /// and this one is about the mechanism: every product in the file arrives,
    /// whatever number that is.
    /// </summary>
    private static readonly int SeedProducts = JsonDocument
        .Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json")))
        .RootElement.GetArrayLength();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Importing_the_seed_writes_products_and_their_events_in_one_transaction()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("outbox_import", ct);

        var result = await scope.Import();

        Assert.Equal(SeedProducts, result.Created);
        Assert.Equal(0, result.Failed);

        await using var context = scope.Factory.Create();
        Assert.Equal(SeedProducts, await context.Products.CountAsync(ct));

        // The events exist already, before any worker runs: that is what it means
        // for the outbox to be transactional and not a queue on the side.
        Assert.NotEmpty(await context.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(ct));

        // A round trip through jsonb: if the LocalizedText converter or the
        // attributes one broke, it would show here and in no unit test.
        var product = await context.Products.FirstAsync(ct);
        Assert.Contains("es", product.Name.Cultures);
        Assert.Contains("en", product.Name.Cultures);
        Assert.NotEmpty(product.ExternalReferences);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Draining_the_outbox_projects_every_published_product()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("outbox_drain", ct);
        await scope.Import();

        // Draft is not indexed (ADR 0012): publishing is what makes a product
        // findable, and without that step the drain should index nothing.
        await using (var context = scope.Factory.Create())
        {
            foreach (var product in await context.Products.ToListAsync(ct))
                product.Publish(Clock);

            await context.SaveChangesAsync(ct);
        }

        var drained = await scope.DrainOutboxAsync(ct);

        Assert.True(drained > 0, "the drain processed nothing, so the events never reached a handler");
        Assert.Equal(SeedProducts, scope.Indexer.Indexed.Count);

        await using var after = scope.Factory.Create();
        Assert.Empty(await after.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(ct));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reimporting_the_same_catalogue_creates_nothing_new()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("outbox_idempotency", ct);

        await scope.Import();
        var second = await scope.Import();

        // The unique index on (source, external_id) is the idempotency key. Only
        // a real Postgres can confirm it.
        Assert.Equal(0, second.Created);
        Assert.Equal(SeedProducts, second.Updated);

        await using var context = scope.Factory.Create();
        Assert.Equal(SeedProducts, await context.Products.CountAsync(ct));
    }

    private async Task<TestScope> ArrangeAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);

        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        return new TestScope(factory);
    }

    /// <summary>
    /// The application's own container, with the indexer swapped out. The real
    /// slices are registered by assembly, exactly as the API does, so this does
    /// not end up testing wiring that only exists in the tests.
    /// </summary>
    private sealed class TestScope : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public TenderoDbContextFactory Factory { get; }
        public RecordingIndexer Indexer { get; } = new();

        public TestScope(TenderoDbContextFactory factory)
        {
            Factory = factory;

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Catalog:Connectors:Seed:FilePath"] =
                        Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json")
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoCqrs(
                typeof(ImportProductsCommand).Assembly,
                typeof(ProductIndexProjection).Assembly);
            services.AddTenderoPersistence(factory.ConnectionString);
            services.AddCatalog(configuration);
            services.AddSingleton<IProductIndexer>(Indexer);

            _provider = services.BuildServiceProvider(validateScopes: true);
        }

        public async Task<ImportProductsResult> Import()
        {
            await using var scope = _provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            return await dispatcher.SendAsync(new ImportProductsCommand("seed"));
        }

        /// <summary>
        /// The same loop as <c>OutboxProcessor</c>, without the timer: read what
        /// is pending, deserialise, dispatch, mark.
        /// </summary>
        public async Task<int> DrainOutboxAsync(CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

            var pending = await context.OutboxMessages
                .Where(message => message.ProcessedAt == null)
                .OrderBy(message => message.OccurredAt)
                .ToListAsync(ct);

            foreach (var message in pending)
            {
                await dispatcher.PublishAsync(
                    DomainEventSerializer.Deserialize(message.Type, message.Payload), ct);
                message.MarkProcessed();
            }

            await context.SaveChangesAsync(ct);
            return pending.Count;
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    /// <summary>
    /// A deterministic double, not a mock: what matters is what reached the port,
    /// and a list says that more clearly than a call verification.
    /// </summary>
    private sealed class RecordingIndexer : IProductIndexer
    {
        /// <summary>Nothing to recreate: this double keeps its documents in a
        /// list, and a list has no mapping to drift.</summary>
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public HashSet<ProductId> Indexed { get; } = [];
        public HashSet<ProductId> Removed { get; } = [];

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
