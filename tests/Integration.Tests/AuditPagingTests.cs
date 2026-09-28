using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The audit screen's reader, paged (R-7.2), where only Postgres can answer: four
/// rows written in the same instant tie on their time and the id keeps page two
/// from repeating one, the outcome narrows before the page, and every outcome is
/// counted over the whole log whichever is shown.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditPagingTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Pages_newest_first_through_a_tie_and_counts_every_outcome_over_the_log()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("audit_paging");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        // Three allowed and two denied, no failure: lopsided, so a count read from
        // the wrong set, or a zero, cannot come out right.
        AuditEntry[] tied =
        [
            Entry(AuditOutcome.Allowed, Instant), Entry(AuditOutcome.Allowed, Instant),
            Entry(AuditOutcome.Denied, Instant), Entry(AuditOutcome.Allowed, Instant),
        ];
        var newest = Entry(AuditOutcome.Denied, Instant.AddMinutes(1));

        // One save each, highest id first: EF orders one save's inserts by key, so
        // this is what makes the table's own order disagree with the tie-break.
        foreach (var entry in tied.OrderByDescending(entry => entry.Id.ToString(), StringComparer.Ordinal).Append(newest))
        {
            await using var context = factory.Create();
            context.AuditEntries.Add(entry);
            await context.SaveChangesAsync(ct);
        }

        var reader = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IAuditReader>();

        var pages = new List<AuditPage>();
        for (var page = 1; page <= 3; page++)
            pages.Add(await reader.PageAsync(null, page, 2, ct));

        // Newest first, then the tie in the id's order: Postgres orders a uuid by
        // its bytes, which is the order of its lower-case text.
        Guid[] expected = [newest.Id, .. tied.Select(entry => entry.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal)];
        Assert.Equal(expected, pages.SelectMany(page => page.Entries).Select(entry => entry.Id));
        Assert.All(pages, page => Assert.Equal(5, page.Total));

        var denied = await reader.PageAsync(AuditOutcome.Denied, 2, 1, ct);
        Assert.Equal(tied[2].Id, Assert.Single(denied.Entries).Id);
        Assert.Equal(2, denied.Total);
        Assert.Equal((3, 2), (denied.CountByOutcome[AuditOutcome.Allowed], denied.CountByOutcome[AuditOutcome.Denied]));
        Assert.False(denied.CountByOutcome.ContainsKey(AuditOutcome.Failed));
    }

    private static AuditEntry Entry(AuditOutcome outcome, DateTimeOffset at) =>
        AuditEntry.For("PublishProductCommand", "{}", CommercePrincipal.Anonymous, outcome, null, null, at);
}
