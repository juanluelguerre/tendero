using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Imported customers against a real Postgres (phase 16, S2b, P16-17): found by
/// where they came from, still found that way once erased — which is what keeps the
/// next import from bringing them back — and never two customers for one store
/// customer, which the unique index holds.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportedCustomersPersistenceTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();
    private static readonly CustomerOrigin Shopify = new("shopify", "6001");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_imported_customer_is_found_by_origin_even_erased_and_never_stored_twice()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("imported_customers");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddTenderoPersistence(factory.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICustomerRepository>()
                .Add(Customer.Imported(Clock, Shopify, "Ana@Example.com", "Ana Ruiz", "es", new MarketingConsent(true, Clock.GetUtcNow(), "imported:shopify"), "kc-ana"));
            await scope.ServiceProvider.GetRequiredService<SharedKernel.IUnitOfWork>().SaveChangesAsync(ct);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var ana = await scope.ServiceProvider.GetRequiredService<IImportedCustomers>().FindByOriginAsync(Shopify, ct);
            Assert.Equal(("ana@example.com", "kc-ana", "imported:shopify"), (ana!.Email, ana.Subject, ana.Marketing!.Wording));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IImportedCustomers>().FindByOriginAsync(new CustomerOrigin("shopify", "6999"), ct));

            // Ordering's question, answered from the same columns: which customer each buyer was imported as.
            var owners = await scope.ServiceProvider.GetRequiredService<Ordering.Ports.IImportedCustomerIds>().ForAsync("shopify", ["6001", "6999"], ct);
            Assert.Equal(new Dictionary<string, SharedKernel.CustomerId> { ["6001"] = ana.Id }, owners);

            ana.Erase(Clock);
            await scope.ServiceProvider.GetRequiredService<SharedKernel.IUnitOfWork>().SaveChangesAsync(ct);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var erased = await scope.ServiceProvider.GetRequiredService<IImportedCustomers>().FindByOriginAsync(Shopify, ct);
            Assert.Equal((true, null), (erased!.IsErased, erased.Email));

            scope.ServiceProvider.GetRequiredService<ICustomerRepository>()
                .Add(Customer.Imported(Clock, Shopify, "otra@example.com", null, "es", null, null));
            await Assert.ThrowsAsync<SharedKernel.ConcurrentWriteException>(() => scope.ServiceProvider.GetRequiredService<SharedKernel.IUnitOfWork>().SaveChangesAsync(ct));
        }
    }
}
