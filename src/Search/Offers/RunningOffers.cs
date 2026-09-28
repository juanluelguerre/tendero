using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Offers;

/// <summary>
/// What the running offers reach, as the search filters on it: category branches
/// and SKUs, or everything. A value that crosses from Pricing and nothing more —
/// the search never holds a promotion.
/// </summary>
internal sealed record OfferScope(bool Everything, IReadOnlyList<string> CategoryCodes, IReadOnlyList<string> Skus)
{
    public static readonly OfferScope Nothing = new(false, [], []);
}

/// <summary>
/// The offers running now, asked when a search runs. A port so the search can
/// be tested without Pricing, and so the crossing into Pricing is one class
/// (<see cref="PricingRunningOffers"/>) that an architecture rule can hold.
/// </summary>
internal interface IRunningOffers
{
    Task<OfferScope> NowAsync(CancellationToken ct = default);

    /// <summary>
    /// What one campaign's promotions reach now (R-8): nothing unless the campaign
    /// exists, is scheduled and its window is open.
    /// </summary>
    Task<OfferScope> CampaignNowAsync(string campaign, CancellationToken ct = default);
}

internal static class RunningOffersExtensions
{
    /// <summary>The asked campaign's reach, or null when no campaign was asked — so nothing is looked up for the searches that do not name one.</summary>
    public static async Task<OfferScope?> CampaignAskedAsync(this IRunningOffers offers, SearchFilters? filters, CancellationToken ct) =>
        filters?.Campaign is { } campaign ? await offers.CampaignNowAsync(campaign, ct) : null;
}

/// <summary>
/// The one place the search reads Pricing. Pricing decides what an offer is
/// (<c>OfferReach</c>) and what a campaign reaches (<c>Campaign.ReachAt</c>, the
/// same rule over its own promotions); this turns its answers into the values the
/// index filters on.
///
/// <paramref name="promotions"/> and <paramref name="campaigns"/> are optional on
/// purpose: the worker and the search evaluation compose the lexical search
/// without Pricing, and in those processes nobody asks for offers — a relevance
/// run is not a shop window. The container fills an unregistered optional
/// parameter with its default, so they get "nothing is on offer" rather than a
/// container that refuses to build.
/// </summary>
internal sealed class PricingRunningOffers(
    TimeProvider clock, IPromotionReader? promotions = null, ICampaignReader? campaigns = null) : IRunningOffers
{
    public async Task<OfferScope> NowAsync(CancellationToken ct = default)
    {
        if (promotions is null)
            return OfferScope.Nothing;

        return Scope(await promotions.OffersRunningAtAsync(clock.GetUtcNow(), ct));
    }

    public async Task<OfferScope> CampaignNowAsync(string campaign, CancellationToken ct = default)
    {
        if (promotions is null || campaigns is null || !CampaignCode.TryParse(campaign, out var code))
            return OfferScope.Nothing;

        var found = await campaigns.FindAsync(code, ct);
        if (found is null)
            return OfferScope.Nothing;

        return Scope(found.ReachAt(await promotions.AllAsync(ct), clock.GetUtcNow()));
    }

    private static OfferScope Scope(OfferReach reach) => new(reach.Everything, reach.CategoryCodes, reach.Skus);
}
