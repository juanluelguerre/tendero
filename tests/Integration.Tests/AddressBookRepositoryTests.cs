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
/// A customer's address book against a real Postgres (P15-2): it round-trips through
/// the repository and the reader — each resolved by its port — with its entries in
/// jsonb, the kernel's <c>Address</c> whole inside them, and its two defaults as
/// columns that a removal clears.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AddressBookRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Address Home = Address.Create("Ana García", "Calle Mayor 12", "3º B", "Madrid", null, "28013", "ES", "+34 600 123 456");
    private static readonly Address Work = Address.Create("Ana García", "Paseo de la Castellana 89", null, "Madrid", "Madrid", "28046", "ES");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_book_round_trips_with_its_entries_in_order_and_its_defaults()
    {
        var factory = await MigratedAsync("accounts_address_book_round_trip");
        var clock = new TestClock(Now);
        var customer = CustomerId.New();
        AddressEntryId home;

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var book = AddressBook.StartFor(customer, clock);
            home = book.Add(clock, "Home", Home, delivery: true, billing: true).Id;
            book.Add(clock, "Work", Work);

            scope.ServiceProvider.GetRequiredService<IAddressBookRepository>().Add(book);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IAddressBookReader>().ForCustomerAsync(customer, Ct);

            Assert.NotNull(read);
            Assert.Equal(["Home", "Work"], read.Entries.Select(entry => entry.Label));
            Assert.Equal(Home, read.Entries[0].Address);
            Assert.Equal(Work, read.Entries[1].Address);
            Assert.Equal((home, home), (read.DeliveryDefault, read.BillingDefault));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAddressBookReader>().ForCustomerAsync(CustomerId.New(), Ct));

            // Changed through the repository and saved: the default removed, and none in its place.
            var stored = await scope.ServiceProvider.GetRequiredService<IAddressBookRepository>().FindAsync(customer, Ct);
            Assert.True(stored!.Remove(clock, home));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using var raw = factory.Create();
        var row = await raw.Database
            .SqlQueryRaw<string>("""
                select coalesce(delivery_default::text, 'none') || ' ' || coalesce(billing_default::text, 'none')
                       || ' ' || jsonb_array_length(entries)::text || ' ' || (entries->0->>'label') as "Value"
                from accounts.address_books
                """)
            .SingleAsync(Ct);
        Assert.Equal("none none 1 Work", row);
    }

    private async Task<TenderoDbContextFactory> MigratedAsync(string name)
    {
        postgres.SkipIfUnavailable();
        var factory = await postgres.CreateDatabaseAsync(name);
        await using var migrate = factory.Create();
        await migrate.Database.MigrateAsync(Ct);
        return factory;
    }

    private static ServiceProvider Services(TenderoDbContextFactory factory) =>
        new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
}
