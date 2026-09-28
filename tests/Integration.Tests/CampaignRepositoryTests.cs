using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Pricing's first table against a real Postgres (ADR 0044): a campaign
/// round-trips with its texts in both languages, its promotions and its picture,
/// the enums as words; the reader answers the scheduled campaigns whose window
/// has not closed, and nothing else.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CampaignRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static LocalizedText Text(string es, string en) => new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private static Campaign NewCampaign(TestClock clock, string code, DateTimeOffset startsAt, int days, CampaignImage? image = null) =>
        Campaign.Create(clock, new CampaignCode(code), new CampaignDetails(
            Text("Tendero Days", "Tendero Days"), Text("Hasta −25 % en ropa", "Up to 25% off clothing"),
            CampaignKind.Event, null, startsAt, startsAt.AddDays(days), 100, ["SUMMER25", "SHIP0"], image));

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_campaign_round_trips_and_the_shop_reads_only_the_scheduled_ones_still_open()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("pricing_campaigns");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var clock = new TestClock(Now);
        var image = new CampaignImage(new ImageId("0f1e2d"), Text("Toldo de rayas", "Striped awning"));
        var running = NewCampaign(clock, "TENDERO_DAYS", Now.AddDays(-1), 7, image);
        running.Schedule(clock);
        var draft = NewCampaign(clock, "DRAFT_ONE", Now.AddDays(3), 7);
        var closing = NewCampaign(clock, "CLOSING", Now.AddDays(-5), 6);
        closing.Schedule(clock);

        await using (var services = new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider())
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICampaignRepository>();
            Assert.False(await repository.AnyAsync(ct));
            repository.Add(running);
            repository.Add(draft);
            repository.Add(closing);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }

        await using var readServices = new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var readScope = readServices.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<ICampaignReader>();

        // CLOSING ends a day after Now: open at Now, closed two days later.
        Assert.Equal(["CLOSING", "TENDERO_DAYS"], (await reader.ScheduledNotOverAsync(Now, ct)).Select(c => c.Code.Value).Order());
        Assert.Equal(["TENDERO_DAYS"], (await reader.ScheduledNotOverAsync(Now.AddDays(2), ct)).Select(c => c.Code.Value));

        var stored = await readScope.ServiceProvider.GetRequiredService<ICampaignRepository>().FindAsync(new CampaignCode("TENDERO_DAYS"), ct);
        Assert.NotNull(stored);
        Assert.Equal(("Up to 25% off clothing", CampaignStatus.Scheduled), (stored.Line.In("en"), stored.Status));
        Assert.Equal(["SUMMER25", "SHIP0"], stored.PromotionCodes);
        Assert.Equal(("0f1e2d", "Striped awning"), (stored.Image!.Id.Value, stored.Image.Alt.In("en")));
        Assert.Null((await reader.FindAsync(new CampaignCode("DRAFT_ONE"), ct))!.Image);

        await using var raw = factory.Create();
        var words = await raw.Database
            .SqlQueryRaw<string>("select status || ' ' || kind as \"Value\" from pricing.campaigns where code = 'TENDERO_DAYS'")
            .SingleAsync(ct);
        Assert.Equal("Scheduled Event", words);
    }
}
