using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A customer's preferences against a real Postgres (P15-6): a chosen language and
/// the marketing consent round-trip in the customer's own row, and a customer who
/// never said comes back with no consent — null, not a consent withdrawn on a
/// default date.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomerPreferencesRepositoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_chosen_language_and_a_consent_round_trip_and_silence_stays_silence()
    {
        postgres.SkipIfUnavailable();
        var factory = await postgres.CreateDatabaseAsync("accounts_preferences_round_trip");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(Ct);

        var clock = new TestClock();
        var ana = Customer.Identified(clock, "ana", "Ana García", "es", Segments.Retail);
        ana.ChooseCulture(clock, "en");
        ana.GiveMarketingConsent(clock, "marketing-2026-10");
        var luis = Customer.Identified(clock, "luis", "Luis", "es", Segments.Retail);

        await using (var context = factory.Create())
        {
            context.Customers.AddRange(ana, luis);
            await context.SaveChangesAsync(Ct);
        }

        await using var read = factory.Create();
        var anaRead = await read.Customers.AsNoTracking().SingleAsync(customer => customer.Id == ana.Id, Ct);
        var luisRead = await read.Customers.AsNoTracking().SingleAsync(customer => customer.Id == luis.Id, Ct);

        Assert.Equal(("en", true), (anaRead.Culture, anaRead.CultureChosen));
        Assert.Equal(new MarketingConsent(true, clock.GetUtcNow(), "marketing-2026-10"), anaRead.Marketing);
        Assert.Null(luisRead.Marketing);
        Assert.False(luisRead.CultureChosen);
    }
}
