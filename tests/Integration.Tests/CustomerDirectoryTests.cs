using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The read the principal accessor makes on every authenticated request:
/// subject in, customer id and segment out.
///
/// It is where a VIP's tariff comes from — nothing else puts the segment on the
/// principal — so the projection has to survive a real Postgres, not only the
/// directory double the accessor's own tests use.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomerDirectoryTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_subject_answers_with_its_customer_and_the_segment_they_are_on()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var provider = await ArrangeAsync("customer_directory_known", ct);

        var marta = Customer.Identified(Clock, "marta", "Marta", "es", Segments.Vip);
        var ana = Customer.Identified(Clock, "ana", "Ana", "es", Segments.Retail);
        await SaveAsync(provider, ct, marta, ana);

        var known = await LookUpAsync(provider, "marta", ct);

        Assert.Equal(new KnownCustomer(marta.Id, Segments.Vip), known);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_subject_the_shop_has_never_seen_answers_with_nobody()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var provider = await ArrangeAsync("customer_directory_unknown", ct);

        await SaveAsync(provider, ct, Customer.Guest(Clock, "es", Segments.Retail));

        Assert.Null(await LookUpAsync(provider, "nobody", ct));
    }

    private static async Task<KnownCustomer?> LookUpAsync(ServiceProvider provider, string subject, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerDirectory>().ForSubjectAsync(subject, ct);
    }

    private static async Task SaveAsync(ServiceProvider provider, CancellationToken ct, params Customer[] customers)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        context.Customers.AddRange(customers);
        await context.SaveChangesAsync(ct);
    }

    private async Task<ServiceProvider> ArrangeAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);

        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs();
        services.AddTenderoPersistence(factory.ConnectionString);

        return services.BuildServiceProvider(validateScopes: true);
    }
}
