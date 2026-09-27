using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.SetProductAttribute;
using ElGuerre.Tendero.Knowledge;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Workers.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// P10-6 end to end, against a real Postgres: a person approves a typed claim,
/// the approval leaves Knowledge through the outbox, the worker turns it into
/// Catalog's own command, and the product holds the value — Knowledge never
/// touching a product (ADR 0018). The product's own <c>ProductUpserted</c> then
/// reindexes it on the next pass, as any other change would — and Catalog's
/// answer is recorded back on the claim, which the specs table matches on.
///
/// The worker's container is composed here the way <c>AddTenderoWorker</c> does
/// it: Catalog's single slice, not its assembly — so a registration missing from
/// the worker would be missing here too.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApprovedClaimToCatalogTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_approved_claim_becomes_the_products_attribute_and_a_refused_one_changes_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("knowledge_to_catalog");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        await using var provider = Compose(factory.ConnectionString);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        ProductId pan;
        await using (var context = factory.Create())
            pan = (await context.Products.FirstAsync(ct)).Id;

        await using (var context = factory.Create())
        {
            context.Claims.Add(Approved(pan, "CAPACITY_L", "3 L"));
            // Vetting would not let this one through; if it ever did, Catalog says no.
            context.Claims.Add(Approved(pan, "MATERIAL", "unobtainium"));
            await context.SaveChangesAsync(ct);
        }

        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        await using var reread = factory.Create();
        var product = await reread.Products.SingleAsync(p => p.Id == pan, ct);
        Assert.Equal(3m, product.AttributeFor("CAPACITY_L")?.Number);
        Assert.NotEqual("unobtainium", product.AttributeFor("MATERIAL")?.RawText);

        // Catalog's answer went back onto each claim, as values.
        var claims = await reread.Claims.Where(claim => claim.ProductId == pan).ToListAsync(ct);
        Assert.Equal("3", claims.Single(claim => claim.AttributeCode == "CAPACITY_L").AppliedAs);
        Assert.Equal("value_rejected", claims.Single(claim => claim.AttributeCode == "MATERIAL").ApplicationRefused);

        var audited = await reread.AuditEntries.Where(entry => entry.CommandType == nameof(SetProductAttributeCommand)).ToListAsync(ct);
        Assert.Equal(2, audited.Count);
        Assert.Single(audited, entry => entry.Outcome == AuditOutcome.Denied);
    }

    private static ProductClaim Approved(ProductId product, string attribute, string value)
    {
        var document = SourceDocument.Describe(product, "es", $"Texto de prueba con {value}.", Clock);
        var claim = ProductClaim.Propose(
            product,
            LocalizedText.From("es", $"Tiene {value}.").With("en", $"It has {value}."),
            attribute, value,
            [new Evidence(document.Id, document.Locate(value)!, 0.95)],
            new Provenance("rules", null, null, null, "run-1", Clock.GetUtcNow()),
            Clock);
        claim.Approve("Ana Tendera", Clock);
        return claim;
    }

    private static ServiceProvider Compose(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json"),
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(ProductIndexProjection).Assembly,
            typeof(RecordDescriptionHandler).Assembly,
            typeof(ApplyApprovedClaimToCatalog).Assembly);
        services.AddTenderoSlices(typeof(SetProductAttributeCommand));
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddKnowledge(configuration);
        services.AddHybridSearch(configuration);
        services.AddSingleton<IProductIndexer>(new NoIndex());
        services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The index takes no part: the lexical projection is the drain tests' business.</summary>
    private sealed class NoIndex : IProductIndexer
    {
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
