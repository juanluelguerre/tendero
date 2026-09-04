using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The read the reindex walks, against a real Postgres.
///
/// **It exists because the reindex endpoint was a guaranteed 500 and every test
/// in the repository was green.** The reader streamed with
/// `AsAsyncEnumerable`, which holds a reader open on the connection for the
/// whole walk; Npgsql has no MARS, so the first question anybody else asked the
/// same `DbContext` mid-walk threw `A command is already in progress`.
///
/// Nobody asked one until phase 4 gave the indexer an `IAvailabilityReader`.
/// Two correct changes, one broken path, and no gate could see it: the unit
/// test's product reader is a list, and a list has no connection.
///
/// So the assertion is not about products. It is that a SECOND query can run
/// while the walk is in progress — which is what projecting one product does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReindexReadTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Walking_the_catalogue_leaves_the_connection_free()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var factory = await postgres.CreateDatabaseAsync("reindex_read");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoPersistence(factory.ConnectionString);
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var name in new[] { "Camisa de lino", "Sartén de hierro", "Mochila urbana" })
                repository.Add(AProduct(name));

            // Each carries a variant, because a product with none is a product
            // whose availability question never reaches the database — and the
            // question is the whole test.

            await unitOfWork.SaveChangesAsync(ct);
        }

        await using var reading = provider.CreateAsyncScope();
        var products = reading.ServiceProvider.GetRequiredService<IProductReader>();

        // The very port that broke it, resolved from the SAME scope — which is
        // the same `DbContext` and therefore the same connection.
        var availability = reading.ServiceProvider.GetRequiredService<IAvailabilityReader>();

        var walked = 0;

        await foreach (var product in products.StreamAllAsync(ct))
        {
            var skus = product.Variants.Select(variant => variant.Sku).ToArray();
            await availability.AvailableAsync(skus, ct);
            walked++;
        }

        Assert.Equal(3, walked);
    }

    private static Product AProduct(string name)
    {
        var product = Product.Create(
            Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = name, ["en"] = name }),
            new Money(29.90m, "EUR"));

        product.AddVariant(Clock, $"{product.Code}-DEFAULT", new Money(29.90m, "EUR"));
        product.Publish(Clock);
        return product;
    }
}
