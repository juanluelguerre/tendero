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
/// A customer's trail against a real Postgres (R-8): it round-trips through the
/// repository and the reader — two classes, each resolved by its port — with its
/// entries in jsonb, the switch as a column, and the retention sweep removing
/// in SQL exactly what the aggregate would have forgotten.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BrowsingHistoryRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_trail_round_trips_and_is_read_most_recent_first()
    {
        var factory = await MigratedAsync("accounts_trail_round_trip");
        var clock = new TestClock(Now.AddHours(-2));
        var customer = CustomerId.New();

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var history = BrowsingHistory.StartFor(customer, clock);
            history.Record(clock, BrowsingKind.View, "B073WXYZ01");
            history.Record(clock, BrowsingKind.Search, "cafetera italiana");
            clock.Advance(TimeSpan.FromHours(1));
            history.Record(clock, BrowsingKind.View, "B07C0FFEE1");

            scope.ServiceProvider.GetRequiredService<IBrowsingHistoryRepository>().Add(history);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var trail = await scope.ServiceProvider.GetRequiredService<IBrowsingHistoryReader>().ForCustomerAsync(customer, Now, Ct);

            Assert.NotNull(trail);
            Assert.True(trail.IsEnabled);
            Assert.Equal(["B07C0FFEE1", "B073WXYZ01"], trail.Views.Select(entry => entry.Key));
            Assert.Equal(Now.AddHours(-1), trail.Views[0].At);
            Assert.Equal(["cafetera italiana"], trail.Searches.Select(entry => entry.Key));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IBrowsingHistoryReader>().ForCustomerAsync(CustomerId.New(), Now, Ct));

            // Changed through the repository and saved: switched off is empty and off in the table.
            var stored = await scope.ServiceProvider.GetRequiredService<IBrowsingHistoryRepository>().FindAsync(customer, Ct);
            stored!.SwitchOff(clock);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using var raw = factory.Create();
        var row = await raw.Database
            .SqlQueryRaw<string>("select is_enabled::text || ' ' || views::text || ' ' || searches::text as \"Value\" from accounts.browsing_histories")
            .SingleAsync(Ct);
        Assert.Equal("false [] []", row);
    }

    /// <summary>
    /// The sweep forgets what the aggregate would: every entry past the cutoff, from
    /// every trail, and nothing newer — the promise kept for customers who never
    /// come back to trigger it.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_retention_sweep_removes_only_what_is_older_than_the_cutoff()
    {
        var factory = await MigratedAsync("accounts_trail_retention");
        var gone = CustomerId.New();
        var mixed = CustomerId.New();
        var recent = CustomerId.New();

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBrowsingHistoryRepository>();

            var old = new TestClock(Now.AddDays(-100));
            var gonesTrail = BrowsingHistory.StartFor(gone, old);
            gonesTrail.Record(old, BrowsingKind.View, "B073WXYZ01");
            gonesTrail.Record(old, BrowsingKind.Search, "mochila");
            repository.Add(gonesTrail);

            // Ninety-five days old: still inside the retention when the later view
            // was written, so the aggregate kept it and only the sweep removes it.
            var older = new TestClock(Now.AddDays(-95));
            var mixedTrail = BrowsingHistory.StartFor(mixed, older);
            mixedTrail.Record(older, BrowsingKind.View, "B073WXYZ01");
            var later = new TestClock(Now.AddDays(-10));
            mixedTrail.Record(later, BrowsingKind.View, "B07C0FFEE1");
            repository.Add(mixedTrail);

            var fresh = new TestClock(Now.AddDays(-1));
            var recentTrail = BrowsingHistory.StartFor(recent, fresh);
            recentTrail.Record(fresh, BrowsingKind.Search, "cafetera");
            repository.Add(recentTrail);

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var swept = await scope.ServiceProvider.GetRequiredService<IBrowsingRetention>().ForgetBeforeAsync(Now - BrowsingHistory.Retention, Ct);
            Assert.Equal(2, swept);
        }

        await using var raw = factory.Create();
        var rows = await raw.Database
            .SqlQueryRaw<string>("select customer_id::text || ' ' || views::text || ' ' || searches::text as \"Value\" from accounts.browsing_histories")
            .ToListAsync(Ct);

        Assert.Contains($"{gone.Value} [] []", rows);
        Assert.Single(rows, row => row.StartsWith(mixed.Value.ToString(), StringComparison.Ordinal)
                                   && row.Contains("B07C0FFEE1", StringComparison.Ordinal)
                                   && !row.Contains("B073WXYZ01", StringComparison.Ordinal));
        Assert.Single(rows, row => row.StartsWith(recent.Value.ToString(), StringComparison.Ordinal) && row.Contains("cafetera", StringComparison.Ordinal));
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
