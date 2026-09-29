using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ListCampaigns;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;
using static ElGuerre.Tendero.Pricing.Tests.Campaigns;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// <c>GET /api/pricing/campaigns</c>: the scheduled campaigns near now, the
/// leading one first, each in the reader's culture and with what it holds.
/// </summary>
public sealed class ListCampaignsHandlerTests
{
    private static readonly Promotion[] Promotions =
    [
        new("SUMMER25", Text("Rebajas de verano", "Summer sale"), new PromotionCondition(CategoryCode: "APPAREL"), new PercentOffLine(25), CombinationPolicy.Stackable, 10),
        new("SHIP0", Text("Envío gratis desde 50 €", "Free shipping over 50 €"), PromotionCondition.Always, new FreeShipping(), CombinationPolicy.Stackable, 40),
        Promotion("BF", new PercentOffLine(30), coupon: "BF2026"),
        Promotion("VIP", new PercentOffLine(10), segment: "vip"),
    ];

    private static Task<ListCampaignsResult> List(DateTimeOffset at, string culture, params Campaign[] campaigns) =>
        new ListCampaignsHandler(new InMemoryCampaigns(campaigns), new GivenPromotions(Promotions), Clock(at))
            .HandleAsync(new ListCampaignsQuery(culture), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_leading_campaign_comes_first_with_its_phase()
    {
        var season = Scheduled("OTONO", Details(startsAt: Now.AddDays(-5), endsAt: Now.AddDays(60), kind: CampaignKind.Season, priority: 1));
        var theEvent = Scheduled("TENDERO_DAYS", Details(priority: 100));

        var result = await List(Now, "es", theEvent, season);

        Assert.Equal(
            [("OTONO", CampaignPhase.Running), ("TENDERO_DAYS", CampaignPhase.Upcoming)],
            result.Items.Select(item => (item.Code, item.Phase)));
    }

    [Fact]
    public async Task It_answers_in_the_readers_culture_and_holds_what_anybody_can_have()
    {
        var image = new CampaignImage(new ImageId("abc"), Text("Toldo de rayas", "Striped awning"));
        var campaign = Scheduled(details: Details(promotions: ["SHIP0", "SUMMER25", "BF", "VIP", "GONE"], image: image));

        var view = Assert.Single((await List(DayAfter, "en", campaign)).Items);

        Assert.Equal(("Tendero Days", "Up to 25% off clothing"), (view.Name, view.Line));
        // In the grouping's order; the coupon promotion is said with its code, the
        // segment one (VIP) is somebody's and not said, a code that names nothing is dropped.
        Assert.Equal(["Free shipping over 50 €", "Summer sale"], view.Holds.Take(2).Select(hold => hold.Name));
        Assert.Equal([null, null, "BF2026"], view.Holds.Select(hold => hold.CouponCode));
        Assert.Equal(("abc", "Striped awning"), (view.ImageId, view.ImageAlt));
        Assert.Equal((CampaignKind.Event, CampaignScope.Shop, (string?)null), (view.Kind, view.Scope, view.CategoryCode));
    }

    [Fact]
    public async Task With_nothing_near_the_list_is_empty()
    {
        var farAway = Scheduled(details: Details(startsAt: Now.AddDays(45)));
        var draft = Draft("DRAFT");

        Assert.Empty((await List(Now, "es", farAway, draft)).Items);
    }

    /// <summary>The home asks on every visit, and most days nothing is near: that answer costs no read of the promotions.</summary>
    [Fact]
    public async Task With_nothing_near_the_promotions_are_not_read()
    {
        var handler = new ListCampaignsHandler(new InMemoryCampaigns(Draft("DRAFT")), new UnreadablePromotions(), Clock(Now));

        var result = await handler.HandleAsync(new ListCampaignsQuery("es"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
    }

    private sealed class UnreadablePromotions : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Nothing is near; the promotions should not have been read.");
    }
}
