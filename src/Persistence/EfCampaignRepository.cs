using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Pricing's campaigns (ADR 0044): the repository the backoffice's commands write
/// through, and the reader the shop's list and the search's filter ask — one
/// class over one table, the two interfaces kept apart as the agents' are.
/// </summary>
internal sealed class EfCampaignRepository(TenderoDbContext context) : ICampaignRepository, ICampaignReader
{
    Task<Campaign?> ICampaignRepository.FindAsync(CampaignCode code, CancellationToken cancellationToken) =>
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

    public async Task<IReadOnlyList<Campaign>> ScheduledNotOverAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        await context.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Scheduled && campaign.EndsAt > at)
            .ToListAsync(cancellationToken);

    Task<Campaign?> ICampaignReader.FindAsync(CampaignCode code, CancellationToken cancellationToken) =>
        context.Campaigns.AsNoTracking().FirstOrDefaultAsync(campaign => campaign.Code == code, cancellationToken);
}
