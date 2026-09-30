using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;
using static ElGuerre.Tendero.Pricing.Tests.Campaigns;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// A campaign's lifecycle (ADR 0044): stored as Draft, Scheduled or Ended, with
/// Running read from the window and the clock — never stored.
/// </summary>
public sealed class CampaignTests
{
    [Fact]
    public void A_new_campaign_is_a_draft_and_says_so()
    {
        var campaign = Draft();

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.True(campaign.IsEditable);
        Assert.Equal(new CampaignCode("TENDERO_DAYS"), Assert.IsType<CampaignCreated>(Assert.Single(campaign.DomainEvents)).Code);
    }

    [Fact]
    public void Its_promotion_codes_and_category_are_kept_as_the_catalogue_writes_codes()
    {
        var campaign = Draft(details: Details(category: " kitchen ", promotions: ["summer25", "SUMMER25", " ship0 "]));

        Assert.Equal(["SUMMER25", "SHIP0"], campaign.PromotionCodes);
        Assert.Equal("KITCHEN", campaign.CategoryCode);
        Assert.Equal(CampaignScope.Category, campaign.Scope);
    }

    [Fact]
    public void Without_a_category_it_is_the_whole_shops()
    {
        Assert.Equal(CampaignScope.Shop, Draft().Scope);
    }

    /// <summary>
    /// A window may be written in the shop's own offset — the seed file writes
    /// Madrid's — and is kept as the same instants in UTC (ADR 0046): Postgres
    /// stores a timestamptz only at offset zero, and the seed's +01:00 stopped the
    /// workers on every empty database.
    /// </summary>
    [Fact]
    public void A_window_written_in_another_offset_is_kept_as_the_same_instants_in_utc()
    {
        var madrid = TimeSpan.FromHours(2);
        var startsAt = DayAfter.ToOffset(madrid);
        var endsAt = DayAfter.AddDays(7).ToOffset(madrid);

        var campaign = Draft(details: Details(startsAt: startsAt, endsAt: endsAt));

        Assert.Equal((DayAfter, DayAfter.AddDays(7), TimeSpan.Zero, TimeSpan.Zero), (campaign.StartsAt, campaign.EndsAt, campaign.StartsAt.Offset, campaign.EndsAt.Offset));
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Draft(details: Details(startsAt: DayAfter, endsAt: DayAfter)));
    }

    [Fact]
    public void A_window_that_has_already_closed_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Draft(details: Details(startsAt: Now.AddDays(-7), endsAt: Now)));
    }

    [Fact]
    public void A_campaign_with_no_promotion_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Draft(details: Details(promotions: [" "])));
    }

    [Fact]
    public void Scheduling_a_draft_puts_it_on_the_calendar()
    {
        var campaign = Draft();
        campaign.ClearDomainEvents();

        campaign.Schedule(Clock());

        Assert.Equal(CampaignStatus.Scheduled, campaign.Status);
        Assert.False(campaign.IsEditable);
        Assert.IsType<CampaignScheduled>(Assert.Single(campaign.DomainEvents));
    }

    [Fact]
    public void A_window_that_closed_since_it_was_set_cannot_be_scheduled()
    {
        var campaign = Draft();

        Assert.Throws<InvalidOperationException>(() => campaign.Schedule(Clock(DayAfter.AddDays(7))));
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
    }

    [Fact]
    public void A_scheduled_campaign_is_edited_only_after_it_is_unscheduled()
    {
        var campaign = Scheduled();

        Assert.Throws<InvalidOperationException>(() => campaign.Update(Clock(), Details(priority: 50)));

        campaign.Unschedule(Clock());
        campaign.Update(Clock(), Details(priority: 50));

        Assert.Equal((CampaignStatus.Draft, 50), (campaign.Status, campaign.Priority));
        Assert.Collection(campaign.DomainEvents,
            e => Assert.IsType<CampaignUnscheduled>(e),
            e => Assert.IsType<CampaignUpdated>(e));
    }

    [Fact]
    public void Ending_a_scheduled_campaign_early_ends_it_now_and_keeps_its_window()
    {
        var campaign = Scheduled();
        var later = Clock(DayAfter.AddDays(2));

        campaign.End(later);

        Assert.Equal(CampaignStatus.Ended, campaign.Status);
        Assert.Equal(later.GetUtcNow(), campaign.EndedAt);
        Assert.Equal(DayAfter.AddDays(7), campaign.EndsAt);
        Assert.Equal(CampaignStatus.Scheduled, Assert.IsType<CampaignEnded>(Assert.Single(campaign.DomainEvents)).From);
    }

    [Fact]
    public void A_draft_can_be_called_off()
    {
        var campaign = Draft();

        campaign.End(Clock());

        Assert.Equal(CampaignStatus.Ended, campaign.Status);
    }

    /// <summary>Ended is terminal: the record of a campaign is not reopened.</summary>
    [Fact]
    public void Nothing_leaves_ended()
    {
        var campaign = Scheduled();
        campaign.End(Clock());

        Assert.False(campaign.CanMoveTo(CampaignStatus.Draft));
        Assert.False(campaign.CanMoveTo(CampaignStatus.Scheduled));
        Assert.Throws<InvalidOperationException>(() => campaign.Schedule(Clock()));
        Assert.Throws<InvalidOperationException>(() => campaign.Unschedule(Clock()));
        Assert.Throws<InvalidOperationException>(() => campaign.End(Clock()));
    }

    [Fact]
    public void A_draft_cannot_be_unscheduled()
    {
        Assert.Throws<InvalidOperationException>(() => Draft().Unschedule(Clock()));
    }

    /// <summary>The window is [start, end): running from its first second, over at its last.</summary>
    [Theory]
    [InlineData(-1, CampaignPhase.Upcoming)]
    [InlineData(0, CampaignPhase.Running)]
    [InlineData(7 * 24 * 60 - 1, CampaignPhase.Running)]
    [InlineData(7 * 24 * 60, CampaignPhase.Over)]
    public void The_phase_is_read_from_the_window_and_the_clock(int minutesFromStart, CampaignPhase phase)
    {
        Assert.Equal(phase, Scheduled().PhaseAt(DayAfter.AddMinutes(minutesFromStart)));
    }

    [Fact]
    public void Running_is_never_stored_the_clock_alone_moves_a_campaign_through_its_window()
    {
        var campaign = Scheduled();

        Assert.False(campaign.IsLiveAt(Now));
        Assert.True(campaign.IsLiveAt(DayAfter));
        Assert.False(campaign.IsLiveAt(DayAfter.AddDays(7)));
        Assert.Equal(CampaignStatus.Scheduled, campaign.Status);
    }

    [Fact]
    public void A_draft_inside_its_window_is_on_no_page()
    {
        var campaign = Draft();

        Assert.Equal(CampaignPhase.Running, campaign.PhaseAt(DayAfter));
        Assert.False(campaign.IsLiveAt(DayAfter));
        Assert.False(campaign.IsNearAt(DayAfter));
    }

    [Fact]
    public void While_it_runs_it_reaches_what_its_own_promotions_reach_and_nothing_else()
    {
        Promotion[] promotions =
        [
            Promotion("SUMMER25", new PercentOffLine(25), condition: new PromotionCondition(CategoryCode: "APPAREL")),
            Promotion("KITCHEN10", new PercentOffLine(10), condition: new PromotionCondition(CategoryCode: "KITCHEN")),
        ];
        var campaign = Scheduled();

        var reach = campaign.ReachAt(promotions, DayAfter);

        Assert.Equal(["APPAREL"], reach.CategoryCodes);
        Assert.True(campaign.ReachAt(promotions, Now).IsNothing);
    }

    /// <summary>The same rule as "on offer": a coupon's discount is not an offer anybody browsing gets.</summary>
    [Fact]
    public void A_promotion_behind_a_coupon_reaches_nothing_even_while_its_campaign_runs()
    {
        Promotion[] promotions = [Promotion("BLACKFRIDAY", new PercentOffLine(30), coupon: "BF2026")];
        var campaign = Scheduled(details: Details(promotions: ["BLACKFRIDAY"]));

        Assert.True(campaign.ReachAt(promotions, DayAfter).IsNothing);
    }

    [Fact]
    public void An_image_is_kept_with_its_alt_text()
    {
        var image = new CampaignImage(new ImageId("abc123"), Text("Toldo de rayas", "Striped awning"));

        var campaign = Draft(details: Details(image: image));

        Assert.Equal(image, campaign.Image);
    }

    [Theory]
    [InlineData("TENDERO_DAYS", true)]
    [InlineData("BLACK_FRIDAY_2026", true)]
    [InlineData("X", true)]
    [InlineData("tendero_days", false)]
    [InlineData("_DAYS", false)]
    [InlineData("TENDERO__DAYS", false)]
    [InlineData("TENDERO-DAYS", false)]
    [InlineData("", false)]
    public void A_campaign_code_is_upper_snake_case(string text, bool valid)
    {
        Assert.Equal(valid, CampaignCode.IsValid(text));
    }

    [Fact]
    public void A_code_is_read_from_a_url_in_any_case()
    {
        Assert.True(CampaignCode.TryParse(" tendero_days ", out var code));
        Assert.Equal("TENDERO_DAYS", code.Value);
        Assert.False(CampaignCode.TryParse("tendero-days", out _));
        Assert.False(CampaignCode.TryParse(null, out _));
    }
}
