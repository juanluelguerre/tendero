using ElGuerre.Tendero.Pricing.Ports;

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
}

/// <summary>
/// The one place the search reads Pricing. Pricing decides what an offer is
/// (<c>OfferReach</c>); this turns its answer into the values the index filters on.
///
/// <paramref name="promotions"/> is optional on purpose: the worker and the
/// search evaluation compose the lexical search without Pricing, and in those
/// processes nobody asks for offers — a relevance run is not a shop window. The
/// container fills an unregistered optional parameter with its default, so they
/// get "nothing is on offer" rather than a container that refuses to build.
/// </summary>
internal sealed class PricingRunningOffers(TimeProvider clock, IPromotionReader? promotions = null) : IRunningOffers
{
    public async Task<OfferScope> NowAsync(CancellationToken ct = default)
    {
        if (promotions is null)
            return OfferScope.Nothing;

        var reach = await promotions.OffersRunningAtAsync(clock.GetUtcNow(), ct);

        return new OfferScope(reach.Everything, reach.CategoryCodes, reach.Skus);
    }
}
