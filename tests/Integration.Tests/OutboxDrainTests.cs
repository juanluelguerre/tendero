using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Importar → outbox → drenaje → proyección, contra Postgres de verdad.
///
/// Cada paso ya tenía tests unitarios y ninguno cubría lo que aquí se rompe:
/// que el volcado de eventos ocurra DENTRO de la transacción que los provocó,
/// que los convertidores jsonb sobrevivan a una ida y vuelta, que el índice
/// único sobre <c>(source, external_id)</c> haga idempotente la reimportación,
/// y que el mensaje deserializado del outbox llegue a su handler.
///
/// Elasticsearch no participa: el indexador es un doble en memoria. Lo que se
/// mide es el mecanismo del outbox, no el motor de búsqueda — de eso ya se
/// ocupa el gate de SearchEval, con un Elasticsearch real.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxDrainTests(PostgresFixture postgres)
{
    private const int SeedProducts = 6;

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

        // Los eventos existen ya, antes de que ningún worker corra: es lo que
        // significa que el outbox sea transaccional y no una cola aparte.
        Assert.NotEmpty(await context.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(ct));

        // Ida y vuelta por jsonb: si el convertidor de LocalizedText o el de
        // atributos se rompiera, se vería aquí y en ningún test unitario.
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

        // Draft no se indexa (ADR 0012): publicar es lo que hace encontrable un
        // producto, y sin este paso el drenaje no debería indexar nada.
        await using (var context = scope.Factory.Create())
        {
            foreach (var product in await context.Products.ToListAsync(ct))
                product.Publish();

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

        // El índice único sobre (source, external_id) es la clave de
        // idempotencia. Sólo un Postgres real puede confirmarlo.
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
    /// El mismo contenedor de la aplicación, con el indexador sustituido. Se
    /// registran los slices reales por assembly, igual que hace la API, para que
    /// esto no acabe probando un cableado que sólo existe en los tests.
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
        /// El mismo bucle que <c>OutboxProcessor</c>, sin el temporizador: leer
        /// pendientes, deserializar, despachar, marcar.
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
    /// Doble determinista, no un mock: lo que importa es qué llegó al puerto,
    /// y una lista lo dice más claro que una verificación de llamadas.
    /// </summary>
    private sealed class RecordingIndexer : IProductIndexer
    {
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
