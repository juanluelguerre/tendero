using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Pricing's campaigns as an aggregate (ADR 0044): what the backoffice's commands
/// load, change and save as a unit. The shop's list and the search's filter read
/// through <see cref="EfCampaignReader"/>, a class of its own — a repository and
/// a reader change for different reasons, as Ordering already keeps them apart
/// (the backlog's "a repository is not a reader").
/// </summary>
internal sealed class EfCampaignRepository(TenderoDbContext context) : ICampaignRepository
{
    public Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken) =>
        context.Campaigns.FirstOrDefaultAsync(campaign => campaign.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Campaign>> AllAsync(CancellationToken cancellationToken = default) =>
        await context.Campaigns
            .AsNoTracking()
            .OrderByDescending(campaign => campaign.StartsAt)
            .ThenBy(campaign => campaign.Code)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        context.Campaigns.AnyAsync(cancellationToken);

    public void Add(Campaign campaign) => context.Campaigns.Add(campaign);
}

/// <summary>
/// Campaigns as the shop reads them: untracked, never saved back. The candidates
/// for the public lineup and one campaign by code for the search's filter.
/// </summary>
internal sealed class EfCampaignReader(TenderoDbContext context) : ICampaignReader
{
    public async Task<IReadOnlyList<Campaign>> ScheduledNotOverAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        await context.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Scheduled && campaign.EndsAt > at)
            .ToListAsync(cancellationToken);

    public Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken = default) =>
        context.Campaigns.AsNoTracking().FirstOrDefaultAsync(campaign => campaign.Code == code, cancellationToken);
}
