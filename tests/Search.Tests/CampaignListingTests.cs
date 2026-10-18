using System.Text.Json.Nodes;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// <c>?campaign=TENDERO_DAYS</c> (R-8): the products a running campaign's
/// promotions reach, by the same query-time rule as "on offer" — no second rule,
/// and nothing when the campaign is not running.
/// </summary>
public sealed class CampaignListingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly DateTimeOffset Opens = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------- The filter ----------

    [Fact]
    public void A_campaign_narrows_every_facet_because_the_listing_is_the_campaign()
    {
        var filters = ProductFilters.From(
            new SearchFilters { Campaign = "TENDERO_DAYS", Brands = ["Vertex"] }, OfferScope.Nothing, Today, new OfferScope(false, ["APPAREL"], []));

        Assert.Contains(FacetCodes.Campaign, filters.Narrowing(FacetCodes.Brand));
        Assert.Contains(FacetCodes.Campaign, filters.Narrowing(FacetCodes.Offer));
        Assert.DoesNotContain(FacetCodes.Campaign, filters.ChosenCodes);
    }

    [Fact]
    public async Task A_running_campaign_asks_for_what_its_promotions_reach()
    {
        var body = await LexicalBody(
            new ProductSearchQuery("", "es", Filters: new SearchFilters { Campaign = "TENDERO_DAYS" }),
            campaign: new OfferScope(false, ["APPAREL"], ["PAN-24"]));

        var postFilter = body["post_filter"]!.ToJsonString();
        Assert.Contains("APPAREL", postFilter);
        Assert.Contains("PAN-24", postFilter);
    }

    [Fact]
    public async Task A_campaign_that_reaches_nothing_lists_nothing_rather_than_the_whole_shop()
    {
        var body = await LexicalBody(new ProductSearchQuery("", "es", Filters: new SearchFilters { Campaign = "OVER" }));

        Assert.Contains("match_none", body["post_filter"]!.ToJsonString());
    }

    [Theory]
    [InlineData("TENDERO_DAYS", true)]
    [InlineData("tendero_days", true)]
    [InlineData("TENDERO-DAYS", false)]
    [InlineData("TENDERO DAYS", false)]
    public void A_campaign_is_asked_for_by_its_code(string campaign, bool valid)
    {
        var result = new SearchFiltersValidator().Validate(new SearchFilters { Campaign = campaign });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void A_campaign_alone_is_a_filter()
    {
        Assert.False(new SearchFilters { Campaign = "TENDERO_DAYS" }.IsEmpty);
    }

    // ---------- The crossing into Pricing ----------

    private static readonly Promotion[] Promotions =
    [
        new("SUMMER25", LocalizedText.From("es", "Rebajas"), new PromotionCondition(CategoryCode: "APPAREL"), new PercentOffLine(25), CombinationPolicy.Stackable, 10),
        new("KITCHEN10", LocalizedText.From("es", "Cocina"), new PromotionCondition(CategoryCode: "KITCHEN"), new PercentOffLine(10), CombinationPolicy.Stackable, 20),
    ];

    private static Campaign TenderoDays(TestClock clock)
    {
        var campaign = Campaign.Create(clock, new CampaignCode("TENDERO_DAYS"), new CampaignDetails(
            LocalizedText.From("es", "Tendero Days"), LocalizedText.From("es", "Hasta −25 %"), CampaignKind.Event, null,
            Opens, Opens.AddDays(7), 100, ["SUMMER25"]));
        campaign.Schedule(clock);
        return campaign;
    }

    [Fact]
    public async Task While_it_runs_the_campaign_reaches_its_own_promotions_branches_asked_in_any_case()
    {
        var clock = new TestClock(Opens.AddDays(-2));
        var offers = new PricingRunningOffers(clock, new GivenPromotions(Promotions), new GivenCampaigns(TenderoDays(clock)));
        clock.Advance(TimeSpan.FromDays(3));

        var scope = await offers.CampaignNowAsync("tendero_days", Ct);

        Assert.Equal(["APPAREL"], scope.CategoryCodes);
    }

    [Fact]
    public async Task Before_it_opens_an_unknown_one_or_a_malformed_code_reach_nothing()
    {
        var clock = new TestClock(Opens.AddDays(-2));
        var offers = new PricingRunningOffers(clock, new GivenPromotions(Promotions), new GivenCampaigns(TenderoDays(clock)));

        ReachesNothing(await offers.CampaignNowAsync("TENDERO_DAYS", Ct));
        ReachesNothing(await offers.CampaignNowAsync("NOBODY", Ct));
        ReachesNothing(await offers.CampaignNowAsync("tendero-days", Ct));
    }

    [Fact]
    public async Task Without_pricing_composed_no_campaign_reaches_anything()
    {
        ReachesNothing(await new PricingRunningOffers(new TestClock()).CampaignNowAsync("TENDERO_DAYS", Ct));
    }

    private static void ReachesNothing(OfferScope scope) =>
        Assert.Equal((false, 0, 0), (scope.Everything, scope.CategoryCodes.Count, scope.Skus.Count));

    private sealed class GivenPromotions(Promotion[] all) : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>(all);
    }

    private sealed class GivenCampaigns(params Campaign[] all) : ICampaignReader
    {
        public Task<IReadOnlyList<Campaign>> ScheduledNotOverAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Campaign>>(all);

        public Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken = default) =>
            Task.FromResult(all.FirstOrDefault(campaign => campaign.Code == code));
    }

    private static async Task<JsonNode> LexicalBody(ProductSearchQuery query, OfferScope? campaign = null)
    {
        var (client, bodies) = ElasticsearchBodies.Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.Lexical(client, campaign: campaign).SearchAsync(query, Ct));

        return JsonNode.Parse(Assert.Single(bodies))!;
    }
}
