using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers;

public sealed class CampaignSeedOptions
{
    public const string SectionName = "Pricing:Campaigns";

    public string FilePath { get; set; } = Path.Combine("seed", "campaigns.sample.json");
}

/// <summary>
/// Puts the sample campaigns on the shop's calendar, once, on a database that has
/// none — so a fresh shop has a season running and an event coming (R-8).
///
/// Campaigns are written by the shopkeeper, so, like stock, they cannot be a file
/// read on every request: the file would undo the backoffice between one page and
/// the next. They are seeded **through the aggregate** — created, then scheduled —
/// so the events a shopkeeper's clicks raise are the events the seed raises, and
/// the rules that refuse a closed window refuse it here too.
///
/// A seed campaign whose window has already closed is skipped with a line in the
/// log rather than failing the start: the file carries dates, and a clone run
/// after them is still a shop that starts.
///
/// **Only when the table is empty**, which keeps it from resetting a calendar
/// somebody has been editing, and only in Development, as the stock seed.
/// </summary>
public sealed class SeedCampaignsAtStartupWorker(
    IServiceScopeFactory scopes,
    IOptions<CampaignSeedOptions> options,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<SeedCampaignsAtStartupWorker> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return;

        var path = options.Value.FilePath;
        if (!File.Exists(path))
        {
            logger.LogInformation("No campaign seed at {Path}; the calendar starts empty", path);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var campaigns = scope.ServiceProvider.GetRequiredService<ICampaignRepository>();

        if (await campaigns.AnyAsync(cancellationToken))
            return;

        var now = clock.GetUtcNow();
        var seeded = 0;

        foreach (var seed in await CampaignSeedFile.LoadAsync(path, cancellationToken))
        {
            if (!Campaign.IsOpenWindow(seed.Details.StartsAt, seed.Details.EndsAt, now))
            {
                logger.LogInformation("Seed campaign {Code} closed at {EndsAt}; skipped", seed.Code, seed.Details.EndsAt);
                continue;
            }

            var campaign = Campaign.Create(clock, seed.Code, seed.Details);
            if (seed.Status == CampaignStatus.Scheduled)
                campaign.Schedule(clock);

            campaigns.Add(campaign);
            seeded++;
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} campaigns from {Path}", seeded, path);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
